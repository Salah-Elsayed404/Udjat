using System.Windows.Forms;
using System.Windows.Threading;
using Udjat.Channels;
using Udjat.Core.Alerts;
using Udjat.Core.Logging;
using Udjat.Core.Monitoring;
using Udjat.Core.Settings;
using Udjat.Core.Status;
using Udjat.Dashboard;
using Udjat.Settings;
using Udjat.Themes;
using Udjat.Tray;
using Udjat.Windows;
using Udjat.Windows.Autostart;
using Udjat.Windows.Secrets;
using Udjat.Windows.Sensors;
using Udjat.Windows.Storage;

namespace Udjat;

/// <summary>
/// The composition root: starts everything in order, keeps the tray icon in step with the monitor, and stops it all
/// cleanly. The rules (what starts when, and what happens when something is missing) are in docs/APP.md.
/// Everything here runs on the UI thread; events from background threads are passed over with <see cref="Ui"/>.
/// </summary>
internal sealed class AppHost : IDisposable
{
    private const string TrayHintId = "tray-hint";
    private static readonly TimeSpan ShutdownWait = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan HistoryWindow = TimeSpan.FromMinutes(30);

    private readonly System.Windows.Application _app;
    private readonly SingleInstance _instance;
    private readonly SettingsStore _store;
    private readonly string _logPath;
    private readonly SecretStore _secrets = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly bool _startHidden;
    private readonly MainHost _dashboard;
    private readonly SettingsController _settingsController;
    private readonly HashSet<string> _dismissedThisRun = [];

    private ReadingHistory _history = new(1);  // replaced at start, once the poll interval is known
    private TrayIcon? _tray;
    private LhmSensorSource? _source;
    private string? _sourceError;
    private LoadResult _load = new(new AppSettings(), null);
    private Pipeline? _pipeline;
    private PollResult? _latest;
    private bool _monitorStopped;
    private bool _emailMissingPassword;
    private bool _telegramMissingToken;
    private bool _autostartRefused;
    private bool _closing;

    public AppHost(System.Windows.Application app, StartOptions options, SingleInstance instance)
    {
        _app = app;
        _instance = instance;
        _startHidden = options.StartHidden;

        var dataFolder = options.DataDir ?? DataFolder.DefaultPath;  // Program.Main has made sure the default folder is safe
        var settingsPath = Path.Combine(dataFolder, "settings.json");
        _logPath = Path.Combine(dataFolder, "alerts.log");
        _store = new SettingsStore(settingsPath);
        _settingsController = new SettingsController(
            _secrets, () => Settings, ChangeSettings, RebuildAsync, BuildChannelsFor,
            () => _latest is null ? null : _source?.CpuTemp, _logPath, Path.GetDirectoryName(settingsPath)!);
        _dashboard = new MainHost(BuildView, DismissNotice, ToggleTheme, _settingsController, () => _closing, ShowTrayHintOnce);
    }

    private AppSettings Settings => _load.Settings;

    public void Start()
    {
        _app.DispatcherUnhandledException += OnUnhandled;
        _app.SessionEnding += (_, _) => Close();  // Windows is logging off or shutting down
        _instance.ShowRequested += () => Ui(ShowDashboard);
        _instance.ExitRequested += () => Ui(Exit);

        _load = _store.Load();
        ThemeManager.Apply(Settings.Theme);
        _history = new ReadingHistory(ReadingHistory.CapacityFor(HistoryWindow, Settings.ToMonitorOptions().PollInterval));
        _tray = new TrayIcon(
            [("Open dashboard", ShowDashboard), ("Settings…", ShowSettings), ("Open alert log", () => ShellOpen.Path(_logPath)), ("", null), ("Exit", Exit)],
            ShowDashboard);
        _tray.Show();

        ReconcileAutostart();
        OpenSensors();
        StartPipeline();
        UpdateTray();

        if (_load.Notice is not null)
            _tray.ShowBalloon("Udjat", _load.Notice, ToolTipIcon.Warning);
        if (!_startHidden)
            ShowDashboard();
    }

    /// <summary>Closes Udjat: stops reading, lets queued alerts go out, then ends the program.</summary>
    public async void Exit()
    {
        if (_closing)
            return;

        _closing = true;
        try
        {
            if (_pipeline is not null)
                await _pipeline.StopAsync();
        }
        catch (Exception)
        {
            // Closing anyway; there is nothing useful to do with an error now.
        }

        _app.Shutdown();
    }

    public void Dispose()
    {
        _tray?.Dispose();
        _source?.Dispose();
        _http.Dispose();
    }

    private void ShowDashboard()
    {
        if (!_closing)
            _dashboard.Show();
    }

    private void ShowSettings()
    {
        if (!_closing)
            _dashboard.ShowPage("Settings");
    }

    private MainView BuildView() => MainView.Build(CurrentStatus(), CurrentNotices(), _latest, Settings, _history);

    /// <summary>The notices for the dashboard, from everything the host knows right now.</summary>
    private IReadOnlyList<Notice> CurrentNotices() =>
        Notices.Build(new NoticeInputs
        {
            SourceError = _sourceError,
            CpuTemp = _latest is null ? null : _source?.CpuTemp,  // not known before the first poll
            SettingsNotice = _load.Notice,
            IsFirstRun = _load.IsNew,
            EmailMissingPassword = _emailMissingPassword,
            TelegramMissingToken = _telegramMissingToken,
            AutostartNeedsInstall = _autostartRefused,
            Dismissed = Settings.DismissedNotices,
        }).Where(notice => !_dismissedThisRun.Contains(notice.Id)).ToList();

    private void DismissNotice(Notice notice)
    {
        if (notice.Dismissal == NoticeDismissal.Forever)
            RememberDismissed(notice.Id);
        else if (notice.Dismissal == NoticeDismissal.ThisRun)
            _dismissedThisRun.Add(notice.Id);

        _dashboard.Refresh();
    }

    /// <summary>Closing the dashboard hides it, so the first time Udjat says where it went.</summary>
    private void ShowTrayHintOnce()
    {
        if (Settings.DismissedNotices.Contains(TrayHintId))
            return;

        _tray?.ShowBalloon("Udjat", "Udjat keeps watching from the tray. Right-click the icon for the menu, or Exit to stop it.", ToolTipIcon.Info);
        RememberDismissed(TrayHintId);
    }

    /// <summary>Stores the id in the settings so the notice (or hint) does not come back.</summary>
    private void RememberDismissed(string id) =>
        ChangeSettings(Settings with { DismissedNotices = [.. Settings.DismissedNotices, id] });

    private void ToggleTheme()
    {
        var next = Settings.Theme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        ChangeSettings(Settings with { Theme = next });
        ThemeManager.Apply(next);
        _dashboard.Refresh();
    }

    /// <summary>Keeps the new settings and writes them to the file. If the file cannot be written they last for this run only.</summary>
    private void ChangeSettings(AppSettings settings)
    {
        _load = _load with { Settings = settings };
        try
        {
            _store.Save(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not saved: the change applies until Udjat is closed.
        }
    }

    /// <summary>Makes the Task Scheduler task match the "start with Windows" setting.</summary>
    private void ReconcileAutostart()
    {
        try
        {
            _autostartRefused = AutostartTask.ReconcileForThisPc(Settings.Autostart) == AutostartChange.Refused;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or IOException)
        {
            // Tried again at the next start. The settings window shows the real error when the user changes the setting.
        }
    }

    private void OpenSensors()
    {
        try
        {
            _source = new LhmSensorSource();
        }
        catch (Exception ex)
        {
            _sourceError = $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    private void StartPipeline()
    {
        if (_source is null || _tray is null)
            return;

        var built = new ChannelFactory(_secrets, _http).Build(Settings, _tray, _app.Dispatcher);
        _emailMissingPassword = built.EmailMissingPassword;
        _telegramMissingToken = built.TelegramMissingToken;
        _pipeline = new Pipeline(Settings, _source, built.Channels, new AlertLog(_logPath), built.Secrets);

        _pipeline.Monitor.Polled += (_, poll) =>
        {
            _history.Add(poll);  // safe from any thread
            Ui(() => { _latest = poll; UpdateTray(); });
        };
        _pipeline.Health.Stopped += (_, _) => Ui(() => { _monitorStopped = true; UpdateTray(); });
        _pipeline.Health.Resumed += (_, _) => Ui(() => { _monitorStopped = false; UpdateTray(); });
        _pipeline.Start();
    }

    /// <summary>Restarts the monitor and the dispatcher with the settings now in use (after Save in the settings page).</summary>
    private async Task RebuildAsync()
    {
        if (_pipeline is not null)
            await _pipeline.StopAsync();

        _pipeline = null;
        StartPipeline();
        UpdateTray();
    }

    /// <summary>The channels for some settings, for the settings page's Test buttons. Null until the tray exists.</summary>
    private BuiltChannels? BuildChannelsFor(AppSettings settings, Func<SecretName, string?> readSecret) =>
        _tray is null ? null : new ChannelFactory(_secrets, _http).Build(settings, _tray, _app.Dispatcher, readSecret);

    private StatusSummary CurrentStatus() => StatusSummary.From(_latest, _monitorStopped, _source is not null, Settings.TemperatureUnit);

    /// <summary>Brings the tray icon and, if it is open, the dashboard up to date.</summary>
    private void UpdateTray()
    {
        if (_closing)
            return;

        _tray?.SetStatus(CurrentStatus());
        _dashboard.Refresh();
    }

    /// <summary>Runs something on the UI thread. Safe to call from any thread, and harmless once Udjat is closing.</summary>
    private void Ui(Action action)
    {
        if (!_closing)
            _app.Dispatcher.BeginInvoke(action);
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A broken window must not end the watchdog.
        e.Handled = true;
        _tray?.ShowBalloon("Udjat", $"Something went wrong in a window ({e.Exception.GetType().Name}). Udjat keeps watching.", ToolTipIcon.Warning);
    }

    /// <summary>Windows is ending the session: there is no time for a polite stop, so wait only a few seconds.</summary>
    private void Close()
    {
        _closing = true;
        if (_pipeline is not null)
            Task.Run(() => _pipeline.StopAsync()).Wait(ShutdownWait);
    }
}

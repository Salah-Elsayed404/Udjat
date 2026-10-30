using Udjat.Channels;
using Udjat.Core.Alerts;
using Udjat.Core.Logging;
using Udjat.Core.Sensors;
using Udjat.Core.Settings;
using Udjat.Core.Status;
using Udjat.Windows.Autostart;

namespace Udjat.Settings;

/// <summary>
/// What the settings page asks of the running app: save (see <see cref="SettingsSaver"/>), send a test message from what is on
/// screen, and open the files and links. The page never touches Credential Manager or Task Scheduler.
/// </summary>
/// <param name="current">The settings Udjat is using now.</param>
/// <param name="apply">Keeps new settings and writes the file.</param>
/// <param name="rebuild">Restarts the monitor and the dispatcher with the new settings.</param>
/// <param name="buildChannels">Makes the channels for some settings, reading secrets from the function it is given. Null while Udjat starts.</param>
/// <param name="cpuTemp">What the sensor source found out about CPU temperature, or null before the first reading.</param>
internal sealed class SettingsController(
    ISecretStore secrets,
    Func<AppSettings> current,
    Action<AppSettings> apply,
    Func<Task> rebuild,
    Func<AppSettings, Func<SecretName, string?>, BuiltChannels?> buildChannels,
    Func<CpuTempStatus?> cpuTemp,
    string logPath,
    string settingsFolder)
{
    private readonly SettingsSaver _saver = new(
        secrets, current, wanted => AutostartTask.ReconcileForThisPc(wanted) == AutostartChange.Refused, apply, rebuild);

    public SettingsModel NewModel()
    {
        var model = new SettingsModel { Version = typeof(SettingsController).Assembly.GetName().Version?.ToString(3) ?? "" };
        Reload(model);
        return model;
    }

    /// <summary>Starts the model again from the settings in use (Discard).</summary>
    public void Reload(SettingsModel model)
    {
        _saver.Reload(model);
        RefreshStatus(model);
    }

    /// <summary>Updates what the General tab says about CPU temperature.</summary>
    public void RefreshStatus(SettingsModel model)
    {
        switch (cpuTemp())
        {
            case null:
                model.SetCpuStatus("Waiting for the first reading…", driverMissing: false);
                break;
            case CpuTempStatus.Available:
                model.SetCpuStatus("Working.", driverMissing: false);
                break;
            case CpuTempStatus.NoValues:
                model.SetCpuStatus("Needs the free PawnIO driver. Install it, then restart Udjat.", driverMissing: true);
                break;
            default:
                model.SetCpuStatus("This processor does not report a temperature that Udjat can read.", driverMissing: false);
                break;
        }
    }

    public async Task SaveAsync(SettingsModel model)
    {
        await _saver.SaveAsync(model);
        RefreshStatus(model);
    }

    /// <summary>
    /// The "Test" button: sends a test message through one channel, using the values on screen even if they are not saved
    /// (and even if the channel's switch is still off). Nothing is saved or logged. The button always comes back, whatever happens.
    /// </summary>
    public async Task TestAsync(ChannelKind kind, SettingsModel model)
    {
        var state = model.Test(kind);
        state.Start();

        try
        {
            var settings = Enabled(model.Form.ToSettings(current()), kind);
            var built = buildChannels(settings, name => _saver.SecretOnScreen(name, model));
            if (built is null)
            {
                state.Finish(false, "Udjat is still starting. Try again in a moment.");
            }
            else if ((kind == ChannelKind.Email && built.EmailMissingPassword) || (kind == ChannelKind.Telegram && built.TelegramMissingToken))
            {
                state.Finish(false, kind == ChannelKind.Email ? "Enter the password first." : "Enter a valid bot token first.");
            }
            else
            {
                var dispatcher = new AlertDispatcher(settings, built.Channels, new AlertLog(logPath), built.Secrets);
                var result = await dispatcher.SendTestAsync(kind, CancellationToken.None);
                state.Finish(result.Success, result.Detail);
            }
        }
        catch (ArgumentException ex)
        {
            state.Finish(false, ex.Message);
        }
        finally
        {
            if (state.Busy)
                state.Finish(false, "The test did not finish. Try again.");  // an unexpected error is still reported by the app
        }
    }

    public void OpenAlertLog() => ShellOpen.Path(logPath);

    public void OpenSettingsFolder() => ShellOpen.Path(settingsFolder);

    public void OpenPawnIo() => ShellOpen.Url(Notices.PawnIoUrl);

    private static AppSettings Enabled(AppSettings settings, ChannelKind kind) => kind switch
    {
        ChannelKind.Email => settings with { Email = settings.Email with { Enabled = true } },
        ChannelKind.Telegram => settings with { Telegram = settings.Telegram with { Enabled = true } },
        _ => settings,
    };
}

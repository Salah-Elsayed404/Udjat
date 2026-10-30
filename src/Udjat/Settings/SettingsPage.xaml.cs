using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Udjat.Core.Alerts;
using Udjat.Core.Settings;

namespace Udjat.Settings;

/// <summary>
/// The Settings page: four tabs of fields and a save bar. The fields are bound to a <see cref="SettingsModel"/>, which keeps
/// the unsaved edits, so they survive switching pages and closing the window. The buttons ask for things through
/// <see cref="SettingsCommands"/>; this class passes them on to the <see cref="SettingsController"/>.
/// </summary>
internal partial class SettingsPage : UserControl
{
    private const string TabGroup = "SettingsTabs";

    private readonly SettingsController _controller;
    private readonly SettingsModel _model;
    private readonly UserControl _alerts = new AlertsTab();
    private readonly UserControl _channels = new ChannelsTab();
    private readonly UserControl _protect = new ProtectTab();
    private readonly UserControl _general = new GeneralTab();

    /// <summary>Raised when the page gains or loses unsaved changes, so the side menu can show a dot.</summary>
    public event Action<bool>? DirtyChanged;

    public SettingsPage(SettingsController controller)
    {
        InitializeComponent();
        _controller = controller;
        _model = controller.NewModel();
        DataContext = _model;  // its own data context: the window's changes every poll, the form must not

        _model.PropertyChanged += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(SettingsModel.IsDirty))
                DirtyChanged?.Invoke(_model.IsDirty);
        };

        foreach (var edited in new[] { TextBoxBase.TextChangedEvent, ToggleButton.CheckedEvent, ToggleButton.UncheckedEvent, PasswordBox.PasswordChangedEvent })
            AddHandler(edited, new RoutedEventHandler(OnEdited));

        AddCommands();

        foreach (var tab in new[] { _alerts, _channels, _protect, _general })
            TabHost.Children.Add(tab);  // all four stay here; only the chosen one is visible

        AlertsTabItem.IsChecked = true;
    }

    private void AddCommands()
    {
        CommandBindings.Add(new CommandBinding(SettingsCommands.Test, async (_, e) => await _controller.TestAsync((ChannelKind)e.Parameter!, _model)));
        CommandBindings.Add(new CommandBinding(SettingsCommands.RemoveSecret, (_, e) => RemoveSecret((string)e.Parameter!)));
        CommandBindings.Add(new CommandBinding(SettingsCommands.ResetAlerts, (_, _) => ResetAlerts()));
        CommandBindings.Add(new CommandBinding(SettingsCommands.OpenLog, (_, _) => _controller.OpenAlertLog()));
        CommandBindings.Add(new CommandBinding(SettingsCommands.OpenFolder, (_, _) => _controller.OpenSettingsFolder()));
        CommandBindings.Add(new CommandBinding(SettingsCommands.OpenPawnIo, (_, _) => _controller.OpenPawnIo()));
    }

    /// <summary>Any edit: the save bar then compares the form with what was saved. Delayed a moment, so the binding has stored the value first.</summary>
    private void OnEdited(object sender, RoutedEventArgs e)
    {
        if (_model.IsLoading || e.OriginalSource is RadioButton { GroupName: TabGroup })
            return;

        Dispatcher.BeginInvoke(_model.Touch, DispatcherPriority.DataBind);
    }

    private void OnTabChecked(object sender, RoutedEventArgs e)
    {
        var tab = (RadioButton)sender;
        _alerts.Visibility = Show(tab == AlertsTabItem);
        _channels.Visibility = Show(tab == ChannelsTabItem);
        _protect.Visibility = Show(tab == ProtectTabItem);
        _general.Visibility = Show(tab == GeneralTabItem);

        if (tab == GeneralTabItem)
            _controller.RefreshStatus(_model);
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        SaveButton.IsEnabled = false;  // saving restarts the monitor, which takes a moment
        try
        {
            await _controller.SaveAsync(_model);
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private void OnDiscard(object sender, RoutedEventArgs e) => _controller.Reload(_model);

    private void RemoveSecret(string which)
    {
        (which == "Smtp" ? _model.SmtpPassword : _model.TelegramToken).Remove();
        _model.Touch();
    }

    /// <summary>Puts the limits and the repeat time back to the defaults. The other tabs are left alone.</summary>
    private void ResetAlerts()
    {
        var defaults = SettingsForm.From(new AppSettings { TemperatureUnit = _model.Form.TemperatureUnit });  // the defaults, in the unit on screen
        foreach (var row in _model.Form.Thresholds)
        {
            if (defaults.Thresholds.FirstOrDefault(d => d.Kind == row.Kind) is not { } original)
                continue;

            (row.Enabled, row.Warning, row.Critical) = (original.Enabled, original.Warning, original.Critical);
        }

        _model.Form.CooldownMinutes = defaults.CooldownMinutes;
        _model.Refresh();
    }
}

using Udjat.Core.Alerts;
using Udjat.Core.Monitoring;
using Udjat.Core.Settings;

namespace Udjat.Core.Protection;

/// <summary>
/// Connects the monitor to <see cref="ProcessGuard"/>: when a sensor has stayed Critical long enough
/// (the monitor raises that once per Critical stay) and auto-protect is on, it closes the allow-listed
/// programs and tells the user what it did. Runs on the polling thread; closing processes is quick.
/// </summary>
/// <param name="notify">Where the "auto-protect acted" message goes, normally <c>AlertDispatcher.Enqueue</c>.</param>
public sealed class AutoProtector(AppSettings settings, ProcessGuard guard, Action<AlertMessage> notify)
{
    public void Attach(SensorMonitor monitor) => monitor.CriticalSustained += OnCriticalSustained;

    private void OnCriticalSustained(object? sender, SensorEvent e)
    {
        if (!settings.AutoProtect.Enabled)
            return;

        var result = guard.Run(settings.AutoProtect.ProcessNames);
        notify(AlertText.AutoProtect(e, result, TimeSpan.FromSeconds(settings.AutoProtect.AfterSeconds), settings.TemperatureUnit));
    }
}

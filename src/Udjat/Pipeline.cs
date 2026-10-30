using Udjat.Core.Alerts;
using Udjat.Core.Logging;
using Udjat.Core.Monitoring;
using Udjat.Core.Protection;
using Udjat.Core.Sensors;
using Udjat.Core.Settings;
using Udjat.Windows.Processes;

namespace Udjat;

/// <summary>
/// The running machinery for one set of settings: the monitor that reads the sensors, the dispatcher that
/// delivers alerts, the check that notices a silent monitor, and auto-protect, all wired together.
/// When the user changes settings, the app stops this and builds a new one.
/// </summary>
internal sealed class Pipeline
{
    private static readonly TimeSpan StopWait = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _monitorStop = new();
    private readonly CancellationTokenSource _dispatcherStop = new();
    private Task? _monitorTask;
    private Task? _dispatcherTask;

    public Pipeline(AppSettings settings, ISensorSource source, IEnumerable<IAlertChannel> channels, AlertLog log, IEnumerable<string> secrets)
    {
        Monitor = new SensorMonitor(source, settings.ToThresholds(), settings.ToMonitorOptions());
        Dispatcher = new AlertDispatcher(settings, channels, log, secrets);
        Health = new MonitorHealth(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(15));

        Dispatcher.Attach(Monitor);
        Dispatcher.Attach(Health);
        Health.Attach(Monitor);
        new AutoProtector(settings, new ProcessGuard(new SystemProcessTable()), Dispatcher.Enqueue).Attach(Monitor);
    }

    public SensorMonitor Monitor { get; }
    public AlertDispatcher Dispatcher { get; }
    public MonitorHealth Health { get; }

    /// <summary>Starts reading and delivering. Call once, after attaching your own handlers.</summary>
    public void Start()
    {
        _dispatcherTask = Task.Run(() => Dispatcher.RunAsync(_dispatcherStop.Token));
        _monitorTask = Task.Run(() => Monitor.RunAsync(_monitorStop.Token));
    }

    /// <summary>
    /// Stops reading, lets the queued alerts go out, then stops delivering. Every wait has a limit,
    /// so a stuck sensor or channel can never keep Udjat from closing.
    /// </summary>
    public async Task StopAsync()
    {
        Health.Dispose();

        await _monitorStop.CancelAsync();
        await FinishedWithinAsync(_monitorTask);  // the poll in progress is allowed to end

        Dispatcher.Complete();  // no more messages will come; the consumer ends when the queue is empty
        if (!await FinishedWithinAsync(_dispatcherTask))
            await _dispatcherStop.CancelAsync();
    }

    private static async Task<bool> FinishedWithinAsync(Task? task)
    {
        if (task is null)
            return true;

        return await Task.WhenAny(task, Task.Delay(StopWait)) == task;
    }
}

using Udjat.Core.Sensors;

namespace Udjat.Core.Monitoring;

/// <summary>
/// Reads the sensors again and again, tracks each one with a <see cref="ThresholdState"/>
/// and raises events. It has no UI or network code: other parts of the app subscribe to the events.
/// Events are raised on the polling thread, so a handler must return quickly (queue slow work).
/// </summary>
public sealed class SensorMonitor
{
    private readonly ISensorSource _source;
    private readonly Dictionary<SensorKind, Threshold> _thresholds;
    private readonly MonitorOptions _options;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, ThresholdState> _states = new();  // one per sensor id

    /// <summary>Settings are fixed for the monitor's lifetime. To change them, build a new monitor.</summary>
    /// <exception cref="ArgumentException">A threshold or an option is out of range.</exception>
    public SensorMonitor(
        ISensorSource source,
        IReadOnlyDictionary<SensorKind, Threshold> thresholds,
        MonitorOptions options,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(thresholds);
        ArgumentNullException.ThrowIfNull(options);

        // Check everything now, so a bad setting fails at startup and not in the middle of a poll.
        options.Validate();
        foreach (var threshold in thresholds.Values)
            threshold.Validate();

        _source = source;
        _thresholds = new Dictionary<SensorKind, Threshold>(thresholds);  // our own copy
        _options = options;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>After every poll, with all readings and their levels.</summary>
    public event EventHandler<PollResult>? Polled;

    /// <summary>A sensor's level rose and the user should be alerted.</summary>
    public event EventHandler<SensorEvent>? AlertRaised;

    /// <summary>A sensor is back to Normal after the user was alerted about it.</summary>
    public event EventHandler<SensorEvent>? Recovered;

    /// <summary>A sensor has stayed Critical long enough for auto-protect. Once per Critical stay.</summary>
    public event EventHandler<SensorEvent>? CriticalSustained;

    /// <summary>The sensor source or an event handler threw. The monitor keeps running.</summary>
    public event EventHandler<Exception>? PollFailed;

    /// <summary>Polls at once, then every <see cref="MonitorOptions.PollInterval"/>, until cancelled.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_options.PollInterval, _time);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            PollOnce();
            while (await timer.WaitForNextTickAsync(cancellationToken))
                PollOnce();
        }
        catch (OperationCanceledException)
        {
            // Cancelling is the normal way to stop, not an error.
        }
    }

    /// <summary>
    /// Reads all sensors once and raises the resulting events. Never throws: a failure is
    /// reported through <see cref="PollFailed"/>. Call it from one thread at a time.
    /// </summary>
    public void PollOnce()
    {
        try
        {
            Poll();
        }
        catch (Exception ex)
        {
            Raise(PollFailed, ex, isFailureEvent: true);
        }
    }

    private void Poll()
    {
        // If the source throws here, nothing has changed yet.
        var readings = _source.ReadAll();
        var now = _time.GetUtcNow();

        var statuses = new List<SensorStatus>();
        var pending = new List<Action>();  // alert-type events, raised after Polled

        foreach (var reading in readings)
        {
            if (!double.IsFinite(reading.Value))
                continue;  // unreadable this time: leave the sensor's state as it was

            statuses.Add(new SensorStatus(reading, Evaluate(reading, now, pending)));
        }

        Raise(Polled, new PollResult(now, statuses));
        foreach (var raise in pending)
            raise();
    }

    /// <summary>Updates one sensor's state and queues any events it caused. Returns its new level.</summary>
    private AlertLevel Evaluate(SensorReading reading, DateTimeOffset now, List<Action> pending)
    {
        if (!_thresholds.TryGetValue(reading.Kind, out var threshold))
            return AlertLevel.Normal;  // no thresholds for this kind (fans): shown, never alerts

        if (!_states.TryGetValue(reading.Id, out var state))
        {
            state = new ThresholdState(threshold, _options);
            _states[reading.Id] = state;
        }

        var update = state.Update(reading.Value, now);
        var sensorEvent = new SensorEvent(reading, update.Current, now);

        if (update.SendAlert)
            pending.Add(() => Raise(AlertRaised, sensorEvent));
        if (update.SendRecovered)
            pending.Add(() => Raise(Recovered, sensorEvent));
        if (update.CriticalSustained)
            pending.Add(() => Raise(CriticalSustained, sensorEvent));

        return update.Current;
    }

    /// <summary>
    /// Calls each subscriber separately, so one that throws cannot stop the others or the poll loop.
    /// The exception is reported through <see cref="PollFailed"/>.
    /// </summary>
    private void Raise<T>(EventHandler<T>? handlers, T args, bool isFailureEvent = false)
    {
        if (handlers is null)
            return;

        foreach (EventHandler<T> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, args);
            }
            catch (Exception ex)
            {
                // A failing PollFailed handler is ignored: there is nowhere left to report it.
                if (!isFailureEvent)
                    Raise(PollFailed, ex, isFailureEvent: true);
            }
        }
    }
}

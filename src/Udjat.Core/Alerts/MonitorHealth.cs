using Udjat.Core.Monitoring;

namespace Udjat.Core.Alerts;

/// <summary>
/// Notices when Udjat has stopped reading the sensors, because a monitor that cannot see is worse than none.
/// It has its own timer, so it also catches a sensor read that never returns.
/// It does not use <see cref="SensorMonitor.PollFailed"/>, which also fires when an event handler has a bug.
/// </summary>
public sealed class MonitorHealth : IDisposable
{
    private readonly TimeSpan _stopAfter;
    private readonly TimeProvider _time;
    private readonly ITimer _timer;
    private readonly object _gate = new();
    private DateTimeOffset _lastPoll;
    private DateTimeOffset _lastCheck;
    private bool _stopped;

    /// <param name="stopAfter">How long without a successful poll counts as stopped (60 s in the settings docs).</param>
    /// <param name="checkEvery">How often the timer looks (15 s). Must be shorter than <paramref name="stopAfter"/>.</param>
    public MonitorHealth(TimeSpan stopAfter, TimeSpan checkEvery, TimeProvider? timeProvider = null)
    {
        if (stopAfter <= TimeSpan.Zero || checkEvery <= TimeSpan.Zero)
            throw new ArgumentException("The times must be greater than zero.");
        if (checkEvery >= stopAfter)
            throw new ArgumentException("The check interval must be shorter than the time that counts as stopped.");

        _stopAfter = stopAfter;
        _time = timeProvider ?? TimeProvider.System;
        _lastPoll = _lastCheck = _time.GetUtcNow();  // a monitor that never polls at all counts as stopped too
        _timer = _time.CreateTimer(_ => Check(), null, checkEvery, checkEvery);
    }

    /// <summary>Raised once when polling stops. The value is the time of the last successful poll.</summary>
    public event EventHandler<DateTimeOffset>? Stopped;

    /// <summary>Raised once when polling works again. The value is the time of that poll.</summary>
    public event EventHandler<DateTimeOffset>? Resumed;

    /// <summary>Counts every successful poll of the monitor.</summary>
    public void Attach(SensorMonitor monitor) => monitor.Polled += (_, _) => OnPolled();

    public void OnPolled()
    {
        DateTimeOffset now;
        bool resumed;
        lock (_gate)
        {
            now = _time.GetUtcNow();
            _lastPoll = now;
            resumed = _stopped;
            _stopped = false;
        }

        if (resumed)
            Raise(Resumed, now);
    }

    /// <summary>Looks whether polling has stopped. The timer calls this; tests can too.</summary>
    public void Check()
    {
        DateTimeOffset since;
        bool stopped;
        lock (_gate)
        {
            var now = _time.GetUtcNow();

            // The timer looks every few seconds. If it has not run for as long as "stopped" takes, the whole
            // PC was asleep (or frozen), and the monitor cannot be blamed: give it a fresh start.
            if (now - _lastCheck >= _stopAfter)
                _lastPoll = now;
            _lastCheck = now;

            since = _lastPoll;
            stopped = !_stopped && now - _lastPoll >= _stopAfter;
            if (stopped)
                _stopped = true;
        }

        if (stopped)
            Raise(Stopped, since);
    }

    public void Dispose() => _timer.Dispose();

    private void Raise(EventHandler<DateTimeOffset>? handlers, DateTimeOffset value)
    {
        if (handlers is null)
            return;

        foreach (EventHandler<DateTimeOffset> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, value);
            }
            catch (Exception)
            {
                // This can run on the timer thread, where an unhandled exception would end the process.
                // A subscriber that fails has nobody left to tell.
            }
        }
    }
}

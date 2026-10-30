using Udjat.Core.Sensors;

namespace Udjat.Tests;

/// <summary>A sensor source whose readings, and failures, the test controls.</summary>
internal sealed class FakeSensorSource : ISensorSource
{
    public IReadOnlyList<SensorReading> Readings { get; set; } = [];

    /// <summary>When set, <see cref="ReadAll"/> throws it.</summary>
    public Exception? Failure { get; set; }

    public int ReadCount { get; private set; }

    public IReadOnlyList<SensorReading> ReadAll()
    {
        ReadCount++;
        if (Failure is not null)
            throw Failure;
        return Readings;
    }
}

/// <summary>
/// A clock that only moves when the test calls <see cref="Advance"/>. Timers made from it
/// (including <see cref="PeriodicTimer"/>) fire as the clock passes their due time.
/// Small on purpose, so the tests need no extra package.
/// </summary>
internal sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    private readonly List<FakeTimer> _timers = [];

    public override DateTimeOffset GetUtcNow() => _now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new FakeTimer(this, callback, state);
        timer.Change(dueTime, period);
        _timers.Add(timer);
        return timer;
    }

    /// <summary>Moves the clock forward, firing every timer that comes due on the way, in order.</summary>
    public void Advance(TimeSpan by)
    {
        var target = _now + by;
        while (_timers.Where(t => t.NextDue <= target).MinBy(t => t.NextDue) is { } next)
        {
            _now = next.NextDue!.Value;
            next.Fire();
        }
        _now = target;
    }

    private sealed class FakeTimer(FakeTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan _period;

        /// <summary>When the timer fires next, or null if it never will.</summary>
        public DateTimeOffset? NextDue { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            NextDue = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
            _period = period;
            return true;
        }

        public void Fire()
        {
            // Reschedule first, so a callback that changes or disposes the timer wins.
            NextDue = _period > TimeSpan.Zero ? NextDue + _period : null;
            callback(state);
        }

        public void Dispose()
        {
            NextDue = null;
            owner._timers.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

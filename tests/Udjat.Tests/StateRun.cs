using Udjat.Core.Monitoring;

namespace Udjat.Tests;

/// <summary>
/// A <see cref="ThresholdState"/> plus a clock, so a test reads as a list of readings.
/// Warning = 80, Critical = 90. Readings arrive 2 s apart unless the test says otherwise.
/// </summary>
internal sealed class StateRun(MonitorOptions? options = null)
{
    public static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly ThresholdState _state = new(new Threshold(80, 90), options ?? new MonitorOptions());
    private DateTimeOffset _now = Start;

    public AlertLevel Level => _state.Level;

    /// <summary>Moves the clock forward, then feeds one reading.</summary>
    public StateUpdate Feed(double value, double secondsLater = 2)
    {
        _now = _now.AddSeconds(secondsLater);
        return _state.Update(value, _now);
    }

    /// <summary>Feeds two readings of the same value (enough to confirm with the defaults) and returns the second.</summary>
    public StateUpdate FeedTwice(double value)
    {
        Feed(value);
        return Feed(value);
    }
}

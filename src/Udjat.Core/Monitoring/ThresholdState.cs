namespace Udjat.Core.Monitoring;

/// <summary>What happened when one new value was fed to a <see cref="ThresholdState"/>.</summary>
/// <param name="Previous">Level before this value.</param>
/// <param name="Current">Level after this value.</param>
/// <param name="SendAlert">The level rose and an alert should go out.</param>
/// <param name="SendRecovered">Back to Normal after an alert was sent: send a "recovered" notice.</param>
/// <param name="CriticalSustained">Critical has now lasted long enough for auto-protect. Once per Critical stay.</param>
public readonly record struct StateUpdate(
    AlertLevel Previous,
    AlertLevel Current,
    bool SendAlert,
    bool SendRecovered,
    bool CriticalSustained);

/// <summary>
/// Tracks one sensor: its current level and when it last alerted.
/// Pure logic with no clock of its own, so it is easy to test. The rules are in docs/ARCHITECTURE.md.
/// </summary>
public sealed class ThresholdState
{
    private readonly Threshold _threshold;
    private readonly MonitorOptions _options;

    private int _warningCount;   // consecutive readings at or above Warning (capped at ConfirmReadings)
    private int _criticalCount;  // consecutive readings at or above Critical (capped at ConfirmReadings)
    private readonly Dictionary<AlertLevel, DateTimeOffset> _lastAlert = new();
    private bool _alertSentInEpisode;  // an episode runs from leaving Normal until returning to Normal
    private DateTimeOffset _criticalSince;
    private bool _sustainedRaised;

    public ThresholdState(Threshold threshold, MonitorOptions options)
    {
        threshold.Validate();
        options.Validate();
        _threshold = threshold;
        _options = options;
    }

    /// <summary>The level after the most recent update. Starts as Normal.</summary>
    public AlertLevel Level { get; private set; } = AlertLevel.Normal;

    /// <summary>Feeds the next reading and returns what, if anything, should happen.</summary>
    public StateUpdate Update(double value, DateTimeOffset now)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), "Value must be a finite number.");

        var previous = Level;
        CountReadings(value);
        Level = NextLevel(previous, value);

        // Alerts only on a rise, and only if this level has not alerted within the cooldown.
        var sendAlert = Level > previous && CooldownPassed(Level, now);
        if (sendAlert)
        {
            _lastAlert[Level] = now;
            _alertSentInEpisode = true;
        }

        // "Recovered" only makes sense if the user was actually told about this episode.
        var sendRecovered = Level == AlertLevel.Normal && previous != AlertLevel.Normal && _alertSentInEpisode;
        if (Level == AlertLevel.Normal)
            _alertSentInEpisode = false;

        return new StateUpdate(previous, Level, sendAlert, sendRecovered, CheckSustained(previous, now));
    }

    /// <summary>Counts readings in a row at or above each limit. A lower reading restarts that count.</summary>
    private void CountReadings(double value)
    {
        var cap = _options.ConfirmReadings;
        _warningCount = value >= _threshold.Warning ? Math.Min(_warningCount + 1, cap) : 0;
        _criticalCount = value >= _threshold.Critical ? Math.Min(_criticalCount + 1, cap) : 0;
    }

    /// <summary>The higher of "what the confirmed readings say" and "what hysteresis lets us hold".</summary>
    private AlertLevel NextLevel(AlertLevel previous, double value)
    {
        var confirm = _options.ConfirmReadings;
        var risen = _criticalCount >= confirm ? AlertLevel.Critical
                  : _warningCount >= confirm ? AlertLevel.Warning
                  : AlertLevel.Normal;

        // Falling is immediate once the value is clear of the limit by the hysteresis margin.
        var held = AlertLevel.Normal;
        if (previous == AlertLevel.Critical && value >= _threshold.Critical - _options.Hysteresis)
            held = AlertLevel.Critical;
        else if (previous != AlertLevel.Normal && value >= _threshold.Warning - _options.Hysteresis)
            held = AlertLevel.Warning;

        return risen > held ? risen : held;
    }

    /// <summary>The cooldown is kept per level, so a recent Warning alert never silences a Critical one.</summary>
    private bool CooldownPassed(AlertLevel level, DateTimeOffset now) =>
        !_lastAlert.TryGetValue(level, out var last) || now - last >= _options.Cooldown;

    /// <summary>True exactly once per Critical stay, when it has lasted <see cref="MonitorOptions.SustainedCriticalAfter"/>.</summary>
    private bool CheckSustained(AlertLevel previous, DateTimeOffset now)
    {
        if (Level != AlertLevel.Critical)
            return false;

        if (previous != AlertLevel.Critical)  // a new Critical stay begins
        {
            _criticalSince = now;
            _sustainedRaised = false;
        }

        if (_sustainedRaised || now - _criticalSince < _options.SustainedCriticalAfter)
            return false;

        _sustainedRaised = true;
        return true;
    }
}

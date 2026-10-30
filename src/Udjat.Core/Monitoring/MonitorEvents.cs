using Udjat.Core.Sensors;

namespace Udjat.Core.Monitoring;

/// <summary>Something that happened to one sensor: an alert, a recovery, or a sustained Critical.</summary>
/// <param name="Reading">The reading that caused it.</param>
/// <param name="Level">The sensor's level at that moment.</param>
/// <param name="Time">When it happened (UTC).</param>
public sealed record SensorEvent(SensorReading Reading, AlertLevel Level, DateTimeOffset Time);

/// <summary>One sensor's reading and level in a single poll.</summary>
public sealed record SensorStatus(SensorReading Reading, AlertLevel Level);

/// <summary>Everything read in one poll. Feeds the dashboard charts and the tray icon colour.</summary>
public sealed record PollResult(DateTimeOffset Time, IReadOnlyList<SensorStatus> Sensors);

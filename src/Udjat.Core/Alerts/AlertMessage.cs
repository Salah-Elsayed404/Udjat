using Udjat.Core.Monitoring;

namespace Udjat.Core.Alerts;

/// <summary>What a message is about. It decides which channels get it.</summary>
public enum AlertMessageKind
{
    /// <summary>A sensor went over its Warning or Critical limit.</summary>
    Alert,

    /// <summary>A sensor is back to normal.</summary>
    Recovered,

    /// <summary>Auto-protect closed (or tried to close) the user's programs.</summary>
    AutoProtect,

    /// <summary>Udjat could not read the sensors for a while.</summary>
    MonitoringStopped,

    /// <summary>Udjat reads the sensors again.</summary>
    MonitoringResumed,

    /// <summary>Sent by the "Test" button of one channel.</summary>
    Test,
}

/// <summary>One message for the user. The wording is built in <see cref="AlertText"/>.</summary>
/// <param name="Kind">What the message is about.</param>
/// <param name="Level">How serious it is (Normal for good news).</param>
/// <param name="Title">One line: the toast title, the email subject, the first line of a Telegram message.</param>
/// <param name="Body">The details.</param>
/// <param name="Time">When it happened (UTC).</param>
/// <param name="Subject">For the log: what was measured, for example "GPU hotspot". Empty if not about one sensor.</param>
/// <param name="Source">For the log: which device, for example "NVIDIA GeForce RTX 3060".</param>
/// <param name="Value">For the log: the value with its unit, for example "108 °C".</param>
public sealed record AlertMessage(
    AlertMessageKind Kind,
    AlertLevel Level,
    string Title,
    string Body,
    DateTimeOffset Time,
    string Subject = "",
    string Source = "",
    string Value = "")
{
    /// <summary>The word that starts a log line.</summary>
    public string Label => Kind switch
    {
        AlertMessageKind.Alert => Level == AlertLevel.Critical ? "CRITICAL" : "WARNING",
        AlertMessageKind.Recovered => "RECOVERED",
        AlertMessageKind.AutoProtect => "AUTO-PROTECT",
        AlertMessageKind.MonitoringStopped => "STOPPED",
        AlertMessageKind.MonitoringResumed => "RESUMED",
        AlertMessageKind.Test => "TEST",
        _ => Kind.ToString().ToUpperInvariant(),
    };
}

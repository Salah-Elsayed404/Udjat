using Udjat.Core.Alerts;
using Udjat.Core.Monitoring;
using Udjat.Core.Sensors;

namespace Udjat.Core.Status;

/// <summary>The overall state, which decides the colour of the tray icon.</summary>
public enum TrayState
{
    Normal,
    Warning,
    Critical,

    /// <summary>Not watching: no sensor reader, polling stopped, or the first poll has not happened yet.</summary>
    NotMonitoring,
}

/// <summary>
/// The overall state and the one line that says it in words (the tray tooltip and the top of the dashboard).
/// Colour is never the only signal.
/// </summary>
public readonly record struct StatusSummary(TrayState State, string Text)
{
    /// <summary>The longest tooltip a Windows tray icon can show.</summary>
    public const int MaxTextLength = 127;

    private const string Prefix = "Udjat: ";

    /// <summary>The same words without the "Udjat: " start, for the dashboard's status pill: "All normal".</summary>
    public string PillText => Text.StartsWith(Prefix, StringComparison.Ordinal) && Text.Length > Prefix.Length
        ? char.ToUpperInvariant(Text[Prefix.Length]) + Text[(Prefix.Length + 1)..]
        : Text;

    public static StatusSummary From(PollResult? latest, bool stopped, bool hasSource, TemperatureUnit unit = TemperatureUnit.Celsius)
    {
        if (!hasSource || stopped)
            return new StatusSummary(TrayState.NotMonitoring, "Udjat: not reading sensors");
        if (latest is null)
            return new StatusSummary(TrayState.NotMonitoring, "Udjat: starting…");

        var critical = latest.Sensors.Where(s => s.Level == AlertLevel.Critical).ToList();
        if (critical.Count > 0)
        {
            var text = critical.Count == 1 ? $"Udjat: {Describe(critical[0], "CRITICAL", unit)}" : $"Udjat: {critical.Count} sensors CRITICAL";
            return new StatusSummary(TrayState.Critical, Cut(text));
        }

        var warnings = latest.Sensors.Where(s => s.Level == AlertLevel.Warning).ToList();
        if (warnings.Count > 0)
        {
            var text = warnings.Count == 1 ? $"Udjat: {Describe(warnings[0], "warning", unit)}" : $"Udjat: {warnings.Count} warnings";
            return new StatusSummary(TrayState.Warning, Cut(text));
        }

        return new StatusSummary(TrayState.Normal, "Udjat: all normal");
    }

    private static string Describe(SensorStatus status, string word, TemperatureUnit unit) =>
        $"{SensorKindInfo.Label(status.Reading.Kind)} {word} ({SensorKindInfo.Format(status.Reading.Kind, status.Reading.Value, unit)})";

    private static string Cut(string text) =>
        text.Length <= MaxTextLength ? text : text[..(MaxTextLength - 1)] + "…";
}

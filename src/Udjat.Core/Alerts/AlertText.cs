using System.Globalization;
using Udjat.Core.Monitoring;
using Udjat.Core.Protection;
using Udjat.Core.Sensors;

namespace Udjat.Core.Alerts;

/// <summary>
/// All the wording the user sees, in one place (English for v1), so translating later means one file.
/// Times are shown in the PC's local time.
/// </summary>
public static class AlertText
{
    public static AlertMessage Alert(SensorEvent e, double limit, TemperatureUnit unit = TemperatureUnit.Celsius)
    {
        var kind = e.Reading.Kind;
        var subject = SensorKindInfo.Label(kind);
        var value = SensorKindInfo.Format(kind, e.Reading.Value, unit);
        var critical = e.Level == AlertLevel.Critical;

        var title = critical ? $"Udjat: {subject} is CRITICAL ({value})" : $"Udjat: {subject} warning ({value})";
        var body = $"{e.Reading.Name}: {subject} is {value}. " +
                   $"{(critical ? "Critical" : "Warning")} limit: {SensorKindInfo.Format(kind, limit, unit)}. {When(e.Time)}.";

        return new AlertMessage(AlertMessageKind.Alert, e.Level, title, body, e.Time, subject, e.Reading.Name, value);
    }

    public static AlertMessage Recovered(SensorEvent e, TemperatureUnit unit = TemperatureUnit.Celsius)
    {
        var kind = e.Reading.Kind;
        var subject = SensorKindInfo.Label(kind);
        var value = SensorKindInfo.Format(kind, e.Reading.Value, unit);

        var title = $"Udjat: {subject} is back to normal ({value})";
        var body = $"{e.Reading.Name}: {subject} is back to normal at {value}. {When(e.Time)}.";

        return new AlertMessage(AlertMessageKind.Recovered, AlertLevel.Normal, title, body, e.Time, subject, e.Reading.Name, value);
    }

    /// <param name="after">How long the sensor had to stay Critical before auto-protect acted.</param>
    public static AlertMessage AutoProtect(SensorEvent e, ProtectResult result, TimeSpan after, TemperatureUnit unit = TemperatureUnit.Celsius)
    {
        var kind = e.Reading.Kind;
        var subject = SensorKindInfo.Label(kind);

        var closed = result.Outcomes
            .Where(o => o.Status == ProcessStatus.Closed)
            .GroupBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"{group.Key}.exe ({group.Count()} {(group.Count() == 1 ? "process" : "processes")})")
            .ToList();

        var title = closed.Count > 0 ? $"Udjat auto-protect closed {string.Join(", ", closed)}"
                  : result.Outcomes.Count > 0 ? "Udjat auto-protect could not close anything"
                  : "Udjat auto-protect found nothing to close";

        var because = $" because the {subject} of {e.Reading.Name} stayed critical for " +
                      $"{after.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} s.";
        var body = title + because + Problems(result) + $" {When(e.Time)}.";

        return new AlertMessage(AlertMessageKind.AutoProtect, AlertLevel.Critical, title, body, e.Time,
            subject, e.Reading.Name, SensorKindInfo.Format(kind, e.Reading.Value, unit));
    }

    /// <summary>What went wrong, if anything: processes that could not be closed, were skipped as protected, or belong to someone else.</summary>
    private static string Problems(ProtectResult result)
    {
        var text = "";

        var failed = result.Outcomes.Where(o => o.Status == ProcessStatus.Failed).ToList();
        if (failed.Count > 0)
            text += " Could not close: " + string.Join("; ", failed.Select(o => $"{o.Name}.exe ({o.Detail})")) + ".";

        var skipped = result.Outcomes.Where(o => o.Status == ProcessStatus.Protected).Select(o => $"{o.Name}.exe").Distinct().ToList();
        if (skipped.Count > 0)
            text += " Skipped as protected: " + string.Join(", ", skipped) + ".";

        var others = result.Outcomes.Where(o => o.Status == ProcessStatus.OtherUser).Select(o => $"{o.Name}.exe").Distinct().ToList();
        if (others.Count > 0)
            text += " Left alone because they run under another account or session: " + string.Join(", ", others) + ".";

        return text;
    }

    /// <param name="since">When the last successful reading was.</param>
    /// <param name="now">When this message is made.</param>
    public static AlertMessage MonitoringStopped(DateTimeOffset since, DateTimeOffset now)
    {
        var title = $"Udjat cannot read the sensors (since {Clock(since)}).";
        var body = $"Udjat has not been able to read the sensors since {Clock(since)}. " +
                   $"It cannot warn you about hardware problems until it reads them again. {When(now)}.";

        return new AlertMessage(AlertMessageKind.MonitoringStopped, AlertLevel.Critical, title, body, now);
    }

    public static AlertMessage MonitoringResumed(DateTimeOffset now) =>
        new(AlertMessageKind.MonitoringResumed, AlertLevel.Normal,
            "Udjat is reading the sensors again.",
            $"Monitoring is working again. {When(now)}.",
            now);

    public static AlertMessage Test(DateTimeOffset now) =>
        new(AlertMessageKind.Test, AlertLevel.Normal,
            "Udjat: test message",
            $"This is a test message from Udjat. If you can read this, this channel works. {When(now)}.",
            now);

    /// <summary>For example "18:03, 29 Oct 2026".</summary>
    public static string When(DateTimeOffset time) =>
        time.ToLocalTime().ToString("HH:mm, d MMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>For example "18:03".</summary>
    public static string Clock(DateTimeOffset time) =>
        time.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
}

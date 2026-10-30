using Udjat.Core.Alerts;
using Udjat.Core.Monitoring;
using Udjat.Core.Sensors;

namespace Udjat.Core.Status;

/// <summary>One fan that is turning: a card on the Fans page.</summary>
/// <param name="RpmText">The speed with a thousands separator and no unit, for example "1,460".</param>
/// <param name="RangeText">What the history says, for example "Last 30 min · 830 – 910 RPM". Empty before the first reading.</param>
public sealed record FanInfo(string Id, string Name, string RpmText, string RangeText);

/// <summary>The fans of one device: the cards of the fans that turn and the names of the ones that do not.</summary>
/// <param name="CountText">"2 turning", "2 turning · 6 stopped" or "2 stopped".</param>
/// <param name="Stopped">Fans at 0 RPM. A motherboard has many empty fan headers, so these are names only, not cards.</param>
/// <param name="Note">Set for a graphics card whose fans are all stopped, because that is normal when it is cool.</param>
public sealed record FanGroup(
    string Title, string Subtitle, string CountText, IReadOnlyList<FanInfo> Fans, IReadOnlyList<string> Stopped, string? Note);

/// <summary>Builds the Fans page from the latest poll and the history. Pure logic, so it is tested.</summary>
public static class FanModel
{
    private const string CoolCardNote = "Graphics cards often stop their fans while they are cool. This is normal.";

    /// <summary>One group per device: every graphics card first, then the other devices (the motherboard's chip, coolers).</summary>
    public static IReadOnlyList<FanGroup> Groups(PollResult poll, ReadingHistory history)
    {
        var groups = new List<FanGroup>();

        foreach (var card in ByDevice(poll, SensorKind.GpuFan))
            groups.Add(Group("Graphics card", card, history, isGraphicsCard: true));

        foreach (var board in ByDevice(poll, SensorKind.Fan))
            groups.Add(Group("Motherboard", board, history, isGraphicsCard: false));

        return groups;
    }

    private static IEnumerable<IGrouping<string, SensorStatus>> ByDevice(PollResult poll, SensorKind kind) =>
        poll.Sensors.Where(s => s.Reading.Kind == kind).GroupBy(s => s.Reading.Device);

    private static FanGroup Group(string title, IGrouping<string, SensorStatus> device, ReadingHistory history, bool isGraphicsCard)
    {
        var ordered = device.OrderBy(f => f.Reading.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var turning = ordered.Where(f => f.Reading.Value > 0).Select(f => Fan(f.Reading, history)).ToList();
        var stopped = ordered.Where(f => f.Reading.Value <= 0).Select(f => f.Reading.Name).ToList();

        return new FanGroup(
            title,
            device.Key,
            Count(turning.Count, stopped.Count),
            turning,
            stopped,
            isGraphicsCard && turning.Count == 0 ? CoolCardNote : null);
    }

    private static FanInfo Fan(SensorReading reading, ReadingHistory history) =>
        new(reading.Id, reading.Name, SensorKindInfo.Thousands(reading.Value), Range(history.Snapshot(reading.Id)));

    private static string Count(int turning, int stopped)
    {
        if (turning == 0)
            return $"{stopped} stopped";

        return stopped == 0 ? $"{turning} turning" : $"{turning} turning · {stopped} stopped";
    }

    /// <summary>"Last 5 min · 830 – 910 RPM", or "Last 5 min · 860 RPM" when the speed did not change.</summary>
    private static string Range(IReadOnlyList<HistoryPoint> points)
    {
        if (points.Count == 0)
            return "";

        var low = SensorKindInfo.Thousands(points.Min(p => p.Value));
        var high = SensorKindInfo.Thousands(points.Max(p => p.Value));
        var minutes = Math.Max(1, (int)Math.Round((points[^1].Time - points[0].Time).TotalMinutes));

        return low == high ? $"Last {minutes} min · {low} RPM" : $"Last {minutes} min · {low} – {high} RPM";
    }
}

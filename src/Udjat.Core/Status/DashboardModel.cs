using System.Globalization;
using Udjat.Core.Alerts;
using Udjat.Core.Monitoring;
using Udjat.Core.Sensors;
using Udjat.Core.Settings;

namespace Udjat.Core.Status;

public enum PanelKind
{
    Processor,
    Graphics,
    Memory,
    Storage,
}

/// <summary>One drive's row in the storage panel.</summary>
/// <param name="Type">"NVMe", or empty when Udjat cannot tell (hard disks and SATA SSDs look alike).</param>
/// <param name="BarPercent">The temperature as a share of the Critical limit, so a full bar means "at the limit".</param>
public sealed record DriveInfo(string Id, string Name, string Type, string ValueText, double BarPercent, AlertLevel Level, string LimitsText)
{
    /// <summary>The name, with the type after it when it is known: "HS-SSD-WAVE(P) 512G · NVMe".</summary>
    public string DisplayName => Type.Length == 0 ? Name : $"{Name} · {Type}";
}

/// <summary>Everything one dashboard panel shows, as plain text and numbers. The window only draws it.</summary>
public sealed record PanelInfo
{
    public required PanelKind Kind { get; init; }
    public required string Title { get; init; }

    /// <summary>The device name (or the total size, for memory).</summary>
    public string Subtitle { get; init; } = "";

    /// <summary>The ring: the load, or for memory the share in use. "—" and no percentage when there is no reading.</summary>
    public string GaugeText { get; init; } = "—";
    public double? GaugePercent { get; init; }

    /// <summary>The big number: the temperature, or for memory how much is in use.</summary>
    public string PrimaryLabel { get; init; } = "";
    public string PrimaryText { get; init; } = "—";
    public AlertLevel PrimaryLevel { get; init; }

    /// <summary>A temperature is coloured by its level even when normal; the memory figure is plain text when normal.</summary>
    public bool PrimaryIsTemperature { get; init; }

    /// <summary>A smaller second number next to the big one (the graphics card's hotspot).</summary>
    public string? SecondaryText { get; init; }
    public AlertLevel SecondaryLevel { get; init; }
    public string LimitsText { get; init; } = "";

    /// <summary>A bar under the ring (the graphics card's video memory).</summary>
    public string? BarLabel { get; init; }
    public string? BarText { get; init; }
    public double? BarPercent { get; init; }
    public AlertLevel BarLevel { get; init; }

    public string? FansText { get; init; }
    public IReadOnlyList<DriveInfo> Drives { get; init; } = [];

    /// <summary>The ids of the readings drawn in the panel's small chart.</summary>
    public string? TemperatureId { get; init; }
    public string? LoadId { get; init; }

    /// <summary>Words for the worst level in the panel, empty when all is normal. Colour is never the only signal.</summary>
    public string LevelText
    {
        get
        {
            var worst = new[] { PrimaryLevel, SecondaryLevel, BarLevel }.Concat(Drives.Select(d => d.Level)).Max();
            return worst switch
            {
                AlertLevel.Critical => "CRITICAL",
                AlertLevel.Warning => "Warning",
                _ => "",
            };
        }
    }
}

/// <summary>Builds what the dashboard shows from the latest poll and the settings. Pure logic, so it is tested.</summary>
public static class DashboardModel
{
    private const string Missing = "—";

    /// <summary>
    /// The panels, in order: one per processor, one per graphics card, memory, storage. A panel with no readings is left out.
    /// The motherboard's fans go in the first processor panel.
    /// </summary>
    public static IReadOnlyList<PanelInfo> Panels(PollResult poll, AppSettings settings)
    {
        var readings = poll.Sensors;
        var panels = new List<PanelInfo>();

        var boardFans = readings.Where(s => s.Reading.Kind == SensorKind.Fan).ToList();
        foreach (var cpu in ByDevice(readings, SensorKind.CpuTemp, SensorKind.CpuLoad))
        {
            panels.Add(Processor(cpu, panels.Count == 0 ? boardFans : [], settings));
        }

        foreach (var gpu in ByDevice(readings, SensorKind.GpuTemp, SensorKind.GpuHotspot, SensorKind.GpuLoad, SensorKind.GpuVram, SensorKind.GpuFan))
            panels.Add(Graphics(gpu, settings));

        if (Find(readings, SensorKind.Ram) is { } ram)
            panels.Add(Memory(ram, settings));

        var drives = readings.Where(s => s.Reading.Kind is SensorKind.NvmeTemp or SensorKind.DriveTemp).ToList();
        if (drives.Count > 0)
            panels.Add(Storage(drives, settings));

        return panels;
    }

    private static PanelInfo Processor(IGrouping<string, SensorStatus> cpu, List<SensorStatus> boardFans, AppSettings settings)
    {
        var temperature = Find(cpu, SensorKind.CpuTemp);
        var load = Find(cpu, SensorKind.CpuLoad);

        return new PanelInfo
        {
            Kind = PanelKind.Processor,
            Title = "Processor",
            Subtitle = cpu.Key,
            GaugeText = Percent(load),
            GaugePercent = load?.Reading.Value,
            PrimaryLabel = "Temperature",
            PrimaryText = Value(temperature, settings),
            PrimaryLevel = temperature?.Level ?? AlertLevel.Normal,
            PrimaryIsTemperature = true,
            LimitsText = Limits(SensorKind.CpuTemp, settings),
            FansText = Fans(boardFans),
            TemperatureId = temperature?.Reading.Id,
            LoadId = load?.Reading.Id,
        };
    }

    private static PanelInfo Graphics(IGrouping<string, SensorStatus> gpu, AppSettings settings)
    {
        var temperature = Find(gpu, SensorKind.GpuTemp);
        var hotspot = Find(gpu, SensorKind.GpuHotspot);
        var load = Find(gpu, SensorKind.GpuLoad);
        var vram = Find(gpu, SensorKind.GpuVram);

        return new PanelInfo
        {
            Kind = PanelKind.Graphics,
            Title = "Graphics card",
            Subtitle = gpu.Key,
            GaugeText = Percent(load),
            GaugePercent = load?.Reading.Value,
            PrimaryLabel = hotspot is null ? "Temperature" : "Temperature · hotspot",
            PrimaryText = Value(temperature, settings),
            PrimaryLevel = temperature?.Level ?? AlertLevel.Normal,
            PrimaryIsTemperature = true,
            SecondaryText = hotspot is null ? null : Value(hotspot, settings),
            SecondaryLevel = hotspot?.Level ?? AlertLevel.Normal,
            LimitsText = Limits(SensorKind.GpuTemp, settings),
            BarLabel = vram is null ? null : "Video memory",
            BarText = vram is null ? null : UsedOfTotal(vram),
            BarPercent = vram?.Reading.Value,
            BarLevel = vram?.Level ?? AlertLevel.Normal,
            FansText = Fans(gpu.Where(s => s.Reading.Kind == SensorKind.GpuFan).ToList()),
            TemperatureId = temperature?.Reading.Id,
            LoadId = load?.Reading.Id,
        };
    }

    private static PanelInfo Memory(SensorStatus ram, AppSettings settings)
    {
        var reading = ram.Reading;
        return new PanelInfo
        {
            Kind = PanelKind.Memory,
            Title = "Memory",
            Subtitle = reading.TotalGb is { } total ? $"{Gb(total)} GB" : "",
            GaugeText = Percent(ram),
            GaugePercent = reading.Value,
            PrimaryLabel = "In use",
            PrimaryText = reading.TotalGb is { } size ? $"{Gb(size * reading.Value / 100)} GB" : Percent(ram),
            PrimaryLevel = ram.Level,
            LimitsText = Limits(SensorKind.Ram, settings),
            LoadId = reading.Id,
        };
    }

    private static PanelInfo Storage(List<SensorStatus> drives, AppSettings settings)
    {
        var rows = drives
            .OrderBy(d => d.Reading.Kind == SensorKind.NvmeTemp ? 0 : 1)
            .ThenBy(d => d.Reading.Name, StringComparer.OrdinalIgnoreCase)
            .Select(d => new DriveInfo(
                d.Reading.Id,
                d.Reading.Name,
                d.Reading.Kind == SensorKind.NvmeTemp ? "NVMe" : "",
                Value(d, settings),
                BarPercent(d, settings),
                d.Level,
                Limits(d.Reading.Kind, settings)))
            .ToList();

        return new PanelInfo
        {
            Kind = PanelKind.Storage,
            Title = "Storage",
            Subtitle = rows.Count == 1 ? "1 drive" : $"{rows.Count} drives",
            Drives = rows,
        };
    }

    /// <summary>Groups readings by device, in the order the devices first appear.</summary>
    private static IEnumerable<IGrouping<string, SensorStatus>> ByDevice(IReadOnlyList<SensorStatus> readings, params SensorKind[] kinds) =>
        readings.Where(s => kinds.Contains(s.Reading.Kind)).GroupBy(s => s.Reading.Device);

    private static SensorStatus? Find(IEnumerable<SensorStatus> readings, SensorKind kind) =>
        readings.FirstOrDefault(s => s.Reading.Kind == kind);

    private static string Value(SensorStatus? status, AppSettings settings) =>
        status is null ? Missing : SensorKindInfo.Format(status.Reading.Kind, status.Reading.Value, settings.TemperatureUnit);

    private static string Percent(SensorStatus? status) =>
        status is null ? Missing : $"{SensorKindInfo.Number(status.Reading.Value)}%";

    /// <summary>For example "10.3 / 12 GB · 86%", or just "86%" when the total is not known.</summary>
    private static string UsedOfTotal(SensorStatus status)
    {
        var reading = status.Reading;
        return reading.TotalGb is { } total
            ? $"{Gb(total * reading.Value / 100)} / {Gb(total)} GB · {SensorKindInfo.Number(reading.Value)}%"
            : $"{SensorKindInfo.Number(reading.Value)}%";
    }

    /// <summary>
    /// The fans that are turning, by name with their speed, and how many are not: "CPU Fan 857 RPM · 4 stopped", or
    /// "2 fans stopped". A motherboard has many headers with nothing plugged in, so listing each stopped fan would bury the useful ones.
    /// </summary>
    private static string? Fans(List<SensorStatus> fans)
    {
        if (fans.Count == 0)
            return null;

        var turning = fans
            .Where(f => f.Reading.Value > 0)
            .OrderBy(f => f.Reading.Name, StringComparer.OrdinalIgnoreCase)
            .Select(f => $"{f.Reading.Name} {SensorKindInfo.Thousands(f.Reading.Value)} RPM")
            .ToList();

        var stopped = fans.Count - turning.Count;
        if (stopped == fans.Count)
            turning.Add(stopped == 1 ? "1 fan stopped" : $"{stopped} fans stopped");
        else if (stopped > 0)
            turning.Add($"{stopped} stopped");

        return string.Join(" · ", turning);
    }

    private static string Gb(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>The temperature as a share of the Critical limit; without limits, the temperature itself against 100.</summary>
    private static double BarPercent(SensorStatus drive, AppSettings settings)
    {
        var limit = settings.Thresholds.TryGetValue(drive.Reading.Kind, out var threshold) && threshold.Enabled ? threshold.Critical : 100;
        return Math.Clamp(drive.Reading.Value / limit * 100, 0, 100);
    }

    private static string Limits(SensorKind kind, AppSettings settings) =>
        !SensorKindInfo.IsDisplayOnly(kind) && settings.Thresholds.TryGetValue(kind, out var limit) && limit.Enabled
            ? $"Warning {SensorKindInfo.Number(SensorKindInfo.Shown(kind, limit.Warning, settings.TemperatureUnit))} · Critical {SensorKindInfo.Number(SensorKindInfo.Shown(kind, limit.Critical, settings.TemperatureUnit))} {SensorKindInfo.Unit(kind, settings.TemperatureUnit)}"
            : "No alert";
}

using Udjat.Core.Monitoring;
using Udjat.Core.Sensors;
using Udjat.Core.Settings;
using Udjat.Core.Status;
using Xunit;

namespace Udjat.Tests;

/// <summary>Tests for <see cref="DashboardModel.Panels"/>: what each dashboard panel says, in words and numbers.</summary>
public class DashboardModelTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 29, 18, 0, 0, TimeSpan.Zero);

    private const string Cpu = "Intel Core i5-13400F";
    private const string Gpu = "NVIDIA GeForce RTX 3060";

    private static SensorStatus S(
        SensorKind kind, double value, string id, string device, string name = "x",
        AlertLevel level = AlertLevel.Normal, double? totalGb = null) =>
        new(new SensorReading(id, kind, name, value, device, totalGb), level);

    private static IReadOnlyList<PanelInfo> Panels(params SensorStatus[] statuses) =>
        DashboardModel.Panels(new PollResult(Start, statuses), new AppSettings());

    private static PanelInfo Panel(PanelKind kind, params SensorStatus[] statuses) =>
        Assert.Single(Panels(statuses), p => p.Kind == kind);

    /// <summary>The owner's PC: processor, graphics card, memory, an NVMe SSD and a hard disk.</summary>
    private static SensorStatus[] FullPc() =>
    [
        S(SensorKind.CpuTemp, 61, "cpu-temp", Cpu, Cpu),
        S(SensorKind.CpuLoad, 12, "cpu-load", Cpu, Cpu),
        S(SensorKind.Fan, 1200, "fan1", "Nuvoton NCT6798D", "Fan #1"),
        S(SensorKind.GpuTemp, 44, "gpu-temp", Gpu, Gpu),
        S(SensorKind.GpuHotspot, 51, "gpu-hotspot", Gpu, Gpu),
        S(SensorKind.GpuLoad, 10, "gpu-load", Gpu, Gpu),
        S(SensorKind.GpuVram, 86, "vram", Gpu, Gpu, totalGb: 12),
        S(SensorKind.GpuFan, 2062, "gpu-fan2", Gpu, "GPU Fan 2"),
        S(SensorKind.GpuFan, 2047, "gpu-fan1", Gpu, "GPU Fan 1"),
        S(SensorKind.Ram, 74, "ram", "System memory", "System memory", totalGb: 16),
        S(SensorKind.NvmeTemp, 45, "nvme", "HS-SSD", "HS-SSD-WAVE(P) 512G"),
        S(SensorKind.DriveTemp, 33, "hdd", "WDC", "WDC WD20EARZ"),
    ];

    // --- The whole PC ---------------------------------------------------------------

    [Fact]
    public void AFullPc_GivesFourPanels_InOrder()
    {
        var panels = Panels(FullPc());

        Assert.Equal([PanelKind.Processor, PanelKind.Graphics, PanelKind.Memory, PanelKind.Storage], panels.Select(p => p.Kind));
        Assert.Equal(["Processor", "Graphics card", "Memory", "Storage"], panels.Select(p => p.Title));
    }

    [Fact]
    public void NoReadings_NoPanels()
    {
        Assert.Empty(Panels());
    }

    // --- Processor ---------------------------------------------------------------------

    [Fact]
    public void Processor_ShowsLoadInTheRing_AndTemperatureAsTheBigNumber()
    {
        var panel = Panel(PanelKind.Processor, FullPc());

        Assert.Equal(Cpu, panel.Subtitle);
        Assert.Equal("12%", panel.GaugeText);
        Assert.Equal(12, panel.GaugePercent);
        Assert.Equal("Temperature", panel.PrimaryLabel);
        Assert.Equal("61 °C", panel.PrimaryText);
        Assert.True(panel.PrimaryIsTemperature);
        Assert.Equal("Warning 90 · Critical 97 °C", panel.LimitsText);
        Assert.Equal("cpu-temp", panel.TemperatureId);
        Assert.Equal("cpu-load", panel.LoadId);
        Assert.Equal("Fan #1 1,200 RPM", panel.FansText);  // the motherboard's fans live here
    }

    [Fact]
    public void Processor_WithoutATemperature_SaysSo_AndKeepsTheLoad()
    {
        var panel = Panel(PanelKind.Processor, S(SensorKind.CpuLoad, 30, "cpu-load", Cpu));

        Assert.Equal("—", panel.PrimaryText);
        Assert.Equal(AlertLevel.Normal, panel.PrimaryLevel);
        Assert.Null(panel.TemperatureId);
        Assert.Equal("30%", panel.GaugeText);
    }

    [Fact]
    public void TheMotherboardFans_GoInTheFirstProcessorPanelOnly()
    {
        var panels = Panels(
            S(SensorKind.CpuTemp, 60, "t1", "CPU one"),
            S(SensorKind.CpuTemp, 62, "t2", "CPU two"),
            S(SensorKind.Fan, 900, "f", "Board", "Fan #1"));

        Assert.Equal("Fan #1 900 RPM", panels[0].FansText);
        Assert.Null(panels[1].FansText);
    }

    // --- Graphics card -----------------------------------------------------------------

    [Fact]
    public void Graphics_ShowsTemperatureAndHotspot_VideoMemoryAndFans()
    {
        var panel = Panel(PanelKind.Graphics, FullPc());

        Assert.Equal(Gpu, panel.Subtitle);
        Assert.Equal("10%", panel.GaugeText);
        Assert.Equal("Temperature · hotspot", panel.PrimaryLabel);
        Assert.Equal("44 °C", panel.PrimaryText);
        Assert.Equal("51 °C", panel.SecondaryText);
        Assert.Equal("Video memory", panel.BarLabel);
        Assert.Equal("10.3 / 12 GB · 86%", panel.BarText);
        Assert.Equal(86, panel.BarPercent);
        Assert.Equal("GPU Fan 1 2,047 RPM · GPU Fan 2 2,062 RPM", panel.FansText);  // sorted by name
        Assert.Equal("gpu-temp", panel.TemperatureId);
        Assert.Equal("gpu-load", panel.LoadId);
    }

    [Fact]
    public void Graphics_WithoutAHotspot_OrVideoMemory_LeavesThemOut()
    {
        var panel = Panel(PanelKind.Graphics, S(SensorKind.GpuTemp, 50, "t", Gpu));

        Assert.Equal("Temperature", panel.PrimaryLabel);
        Assert.Null(panel.SecondaryText);
        Assert.Null(panel.BarText);
        Assert.Null(panel.BarPercent);
        Assert.Null(panel.FansText);
    }

    [Fact]
    public void VideoMemory_WithoutATotal_ShowsOnlyThePercentage()
    {
        var panel = Panel(PanelKind.Graphics, S(SensorKind.GpuVram, 40, "vram", Gpu));

        Assert.Equal("40%", panel.BarText);
    }

    [Fact]
    public void Fans_ListTheOnesThatTurn_AndCountTheStoppedOnes()
    {
        var panel = Panel(PanelKind.Processor,
            S(SensorKind.CpuTemp, 50, "t", Cpu),
            S(SensorKind.Fan, 857, "a", "Board", "CPU Fan"),
            S(SensorKind.Fan, 0, "b", "Board", "Pump Fan"),
            S(SensorKind.Fan, 860, "c", "Board", "System Fan #1"),
            S(SensorKind.Fan, 0, "d", "Board", "System Fan #2"));

        Assert.Equal("CPU Fan 857 RPM · System Fan #1 860 RPM · 2 stopped", panel.FansText);
    }

    [Theory]
    [InlineData(1, "1 fan stopped")]
    [InlineData(2, "2 fans stopped")]
    public void Fans_WhenNoneTurn_SayHowManyAreStopped(int count, string expected)
    {
        var fans = Enumerable.Range(0, count).Select(i => S(SensorKind.GpuFan, 0, $"f{i}", Gpu, $"GPU Fan {i}")).ToList();

        var panel = Panel(PanelKind.Graphics, [S(SensorKind.GpuTemp, 40, "t", Gpu), .. fans]);

        Assert.Equal(expected, panel.FansText);
    }

    [Fact]
    public void TwoGraphicsCards_GetTwoPanels_EachWithItsOwnFans()
    {
        var panels = Panels(
            S(SensorKind.GpuTemp, 50, "a", "Card A"), S(SensorKind.GpuFan, 1000, "fa", "Card A", "Fan"),
            S(SensorKind.GpuTemp, 60, "b", "Card B"), S(SensorKind.GpuFan, 2000, "fb", "Card B", "Fan"));

        Assert.Equal(["Card A", "Card B"], panels.Select(p => p.Subtitle));
        Assert.Equal(["Fan 1,000 RPM", "Fan 2,000 RPM"], panels.Select(p => p.FansText));
    }

    // --- Memory --------------------------------------------------------------------------

    [Fact]
    public void Memory_ShowsTheShareInTheRing_AndGigabytesInUse()
    {
        var panel = Panel(PanelKind.Memory, FullPc());

        Assert.Equal("16 GB", panel.Subtitle);
        Assert.Equal("74%", panel.GaugeText);
        Assert.Equal("In use", panel.PrimaryLabel);
        Assert.Equal("11.8 GB", panel.PrimaryText);  // 74 % of 16 GB
        Assert.False(panel.PrimaryIsTemperature);
        Assert.Equal("Warning 97 · Critical 99 %", panel.LimitsText);
        Assert.Equal("ram", panel.LoadId);
        Assert.Null(panel.TemperatureId);
    }

    [Fact]
    public void Memory_WithoutATotal_ShowsThePercentage()
    {
        var panel = Panel(PanelKind.Memory, S(SensorKind.Ram, 74, "ram", "System memory"));

        Assert.Equal("", panel.Subtitle);
        Assert.Equal("74%", panel.PrimaryText);
    }

    // --- Storage ---------------------------------------------------------------------------

    [Fact]
    public void Storage_ListsEachDrive_NvmeFirst_WithABarTowardsItsCriticalLimit()
    {
        var panel = Panel(PanelKind.Storage, FullPc());

        Assert.Equal("2 drives", panel.Subtitle);
        var nvme = panel.Drives[0];
        Assert.Equal("HS-SSD-WAVE(P) 512G", nvme.Name);
        Assert.Equal("NVMe", nvme.Type);
        Assert.Equal("HS-SSD-WAVE(P) 512G · NVMe", nvme.DisplayName);
        Assert.Equal("45 °C", nvme.ValueText);
        Assert.Equal(45.0 / 82 * 100, nvme.BarPercent, 6);
        Assert.Equal("Warning 75 · Critical 82 °C", nvme.LimitsText);

        var hdd = panel.Drives[1];
        Assert.Equal("", hdd.Type);
        Assert.Equal("WDC WD20EARZ", hdd.DisplayName);  // no type known, so nothing is added
        Assert.Equal(60, hdd.BarPercent, 6);  // 33 of 55
    }

    [Fact]
    public void Storage_WithOneDrive_SaysDriveInTheSingular_AndWithNoDrives_IsLeftOut()
    {
        Assert.Equal("1 drive", Panel(PanelKind.Storage, S(SensorKind.DriveTemp, 30, "d", "Disk")).Subtitle);
        Assert.DoesNotContain(Panels(S(SensorKind.Ram, 50, "ram", "m")), p => p.Kind == PanelKind.Storage);
    }

    [Fact]
    public void ADriveBar_NeverExceedsAFullBar()
    {
        var panel = Panel(PanelKind.Storage, S(SensorKind.NvmeTemp, 95, "d", "Disk"));

        Assert.Equal(100, panel.Drives[0].BarPercent);
    }

    // --- Words for the level -----------------------------------------------------------------

    [Fact]
    public void LevelText_NamesTheWorstLevelInThePanel_AndIsEmptyWhenAllIsNormal()
    {
        Assert.Equal("", Panel(PanelKind.Graphics, FullPc()).LevelText);

        var hotspot = Panel(PanelKind.Graphics,
            S(SensorKind.GpuTemp, 44, "t", Gpu),
            S(SensorKind.GpuHotspot, 109, "h", Gpu, level: AlertLevel.Critical));
        var drive = Panel(PanelKind.Storage, S(SensorKind.DriveTemp, 52, "d", "Disk", level: AlertLevel.Warning));

        Assert.Equal("CRITICAL", hotspot.LevelText);
        Assert.Equal("Warning", drive.LevelText);
    }

    [Fact]
    public void ABigNumber_CarriesItsLevel()
    {
        var panel = Panel(PanelKind.Processor, S(SensorKind.CpuTemp, 95, "t", Cpu, level: AlertLevel.Warning));

        Assert.Equal(AlertLevel.Warning, panel.PrimaryLevel);
    }
}

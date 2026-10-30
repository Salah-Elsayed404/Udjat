using Udjat.Core.Alerts;
using Udjat.Core.Monitoring;
using Udjat.Core.Sensors;
using Udjat.Core.Status;
using Xunit;

namespace Udjat.Tests;

/// <summary>Tests for <see cref="FanModel.Groups"/>: what the Fans page says about each device's fans.</summary>
public class FanModelTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private const string Gpu = "NVIDIA GeForce RTX 3060";
    private const string Board = "Nuvoton NCT6798D";

    private static SensorStatus Fan(SensorKind kind, string name, double rpm, string device) =>
        new(new SensorReading(name, kind, name, rpm, device), AlertLevel.Normal);

    private static SensorStatus GpuFan(string name, double rpm) => Fan(SensorKind.GpuFan, name, rpm, Gpu);

    private static SensorStatus BoardFan(string name, double rpm) => Fan(SensorKind.Fan, name, rpm, Board);

    private static IReadOnlyList<FanGroup> Groups(params SensorStatus[] statuses) =>
        FanModel.Groups(new PollResult(Start, statuses), new ReadingHistory(900));

    // --- Which groups, in what order -----------------------------------------------------

    [Fact]
    public void NoFans_NoGroups()
    {
        Assert.Empty(Groups());
        Assert.Empty(Groups(new SensorStatus(new SensorReading("t", SensorKind.CpuTemp, "CPU", 50), AlertLevel.Normal)));
    }

    [Fact]
    public void GraphicsCardsComeFirst_ThenTheMotherboard()
    {
        var groups = Groups(BoardFan("CPU Fan", 857), GpuFan("GPU Fan 1", 1460));

        Assert.Equal(["Graphics card", "Motherboard"], groups.Select(g => g.Title));
        Assert.Equal([Gpu, Board], groups.Select(g => g.Subtitle));
    }

    [Fact]
    public void TwoGraphicsCards_GiveTwoGroups()
    {
        var groups = Groups(
            GpuFan("GPU Fan 1", 1000),
            Fan(SensorKind.GpuFan, "GPU Fan 1", 900, "AMD Radeon RX 7800 XT"));

        Assert.Equal([Gpu, "AMD Radeon RX 7800 XT"], groups.Select(g => g.Subtitle));
    }

    // --- Turning and stopped fans ---------------------------------------------------------

    [Fact]
    public void FansThatTurn_GetACard_AndStoppedOnesAreOnlyNames()
    {
        var group = Assert.Single(Groups(
            BoardFan("System Fan #2", 0),
            BoardFan("CPU Fan", 857),
            BoardFan("Pump Fan", 0),
            BoardFan("System Fan #1", 860)));

        Assert.Equal(["CPU Fan", "System Fan #1"], group.Fans.Select(f => f.Name));
        Assert.Equal(["Pump Fan", "System Fan #2"], group.Stopped);
        Assert.Equal("2 turning · 2 stopped", group.CountText);
        Assert.Null(group.Note);
    }

    [Theory]
    [InlineData(1460, "1,460")]
    [InlineData(857.4, "857")]
    [InlineData(12000, "12,000")]
    public void TheSpeed_HasAThousandsSeparator_AndNoUnit(double rpm, string expected)
    {
        var group = Assert.Single(Groups(GpuFan("GPU Fan 1", rpm)));

        Assert.Equal(expected, Assert.Single(group.Fans).RpmText);
    }

    [Fact]
    public void AllTurning_SaysOnlyHowManyTurn()
    {
        var group = Assert.Single(Groups(GpuFan("GPU Fan 1", 1400), GpuFan("GPU Fan 2", 1400)));

        Assert.Equal("2 turning", group.CountText);
        Assert.Empty(group.Stopped);
    }

    [Fact]
    public void ACoolGraphicsCard_WithAllFansStopped_GetsAReassuringNote()
    {
        var group = Assert.Single(Groups(GpuFan("GPU Fan 1", 0), GpuFan("GPU Fan 2", 0)));

        Assert.Empty(group.Fans);
        Assert.Equal("2 stopped", group.CountText);
        Assert.Contains("normal", group.Note);
    }

    [Fact]
    public void AMotherboardWithNothingTurning_GetsNoNote()
    {
        var group = Assert.Single(Groups(BoardFan("Pump Fan", 0)));

        Assert.Equal("1 stopped", group.CountText);
        Assert.Null(group.Note);
    }

    // --- The range from the history -------------------------------------------------------

    private static FanInfo OneFanWithHistory(params (int Second, double Rpm)[] samples)
    {
        var history = new ReadingHistory(900);
        foreach (var (second, rpm) in samples)
            history.Add(new PollResult(Start.AddSeconds(second), [BoardFan("CPU Fan", rpm)]));

        var latest = samples[^1];
        var poll = new PollResult(Start.AddSeconds(latest.Second), [BoardFan("CPU Fan", latest.Rpm)]);
        return Assert.Single(Assert.Single(FanModel.Groups(poll, history)).Fans);
    }

    [Fact]
    public void TheRange_ShowsTheLowestAndHighestSpeedAndHowLongItCovers()
    {
        var fan = OneFanWithHistory((0, 830), (300, 910), (600, 857));

        Assert.Equal("Last 10 min · 830 – 910 RPM", fan.RangeText);
    }

    [Fact]
    public void ASteadyFan_ShowsOneSpeed()
    {
        var fan = OneFanWithHistory((0, 860), (120, 860.2), (240, 859.8));

        Assert.Equal("Last 4 min · 860 RPM", fan.RangeText);
    }

    [Fact]
    public void ABrandNewHistory_SaysAtLeastOneMinute()
    {
        var fan = OneFanWithHistory((0, 860), (4, 861));

        Assert.StartsWith("Last 1 min", fan.RangeText);
    }

    [Fact]
    public void WithoutHistory_TheRangeIsEmpty()
    {
        var group = Assert.Single(Groups(BoardFan("CPU Fan", 857)));

        Assert.Equal("", Assert.Single(group.Fans).RangeText);
    }
}

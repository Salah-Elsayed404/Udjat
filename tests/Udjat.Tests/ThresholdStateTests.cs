using Udjat.Core.Monitoring;
using Xunit;

namespace Udjat.Tests;

/// <summary>
/// Tests for the per-sensor state machine: rising, falling and bad input.
/// Unless a test says otherwise: Warning = 80, Critical = 90, hysteresis 3 and two readings to confirm.
/// See <see cref="StateRun"/> for how readings are fed in.
/// </summary>
public class ThresholdStateTests
{
    // --- Rising -------------------------------------------------------------

    [Fact]
    public void SingleSpike_IsIgnored()
    {
        var run = new StateRun();

        var spike = run.Feed(95);
        var back = run.Feed(70);

        Assert.Equal(AlertLevel.Normal, spike.Current);
        Assert.Equal(AlertLevel.Normal, back.Current);
        Assert.False(spike.SendAlert);
    }

    [Fact]
    public void ReadingsThatAreNotInARow_DoNotCount()
    {
        var run = new StateRun();

        run.Feed(95);
        run.Feed(70);
        var update = run.Feed(95);

        Assert.Equal(AlertLevel.Normal, update.Current);
    }

    [Fact]
    public void Rises_OnceEnoughReadingsConfirm()
    {
        var run = new StateRun();

        var first = run.Feed(85);
        var second = run.Feed(85);

        Assert.Equal(AlertLevel.Normal, first.Current);
        Assert.Equal(AlertLevel.Warning, second.Current);
        Assert.Equal(AlertLevel.Normal, second.Previous);
        Assert.True(second.SendAlert);
    }

    [Fact]
    public void ValueExactlyAtLimit_CountsAsOver()
    {
        var run = new StateRun();

        var update = run.FeedTwice(80);

        Assert.Equal(AlertLevel.Warning, update.Current);
    }

    [Fact]
    public void ConfirmReadingsOfOne_RisesImmediately()
    {
        var run = new StateRun(new MonitorOptions { ConfirmReadings = 1 });

        var update = run.Feed(95);

        Assert.Equal(AlertLevel.Critical, update.Current);
        Assert.True(update.SendAlert);
    }

    [Fact]
    public void NormalToCritical_SendsOneAlert()
    {
        var run = new StateRun();

        var first = run.Feed(95);
        var second = run.Feed(95);

        Assert.False(first.SendAlert);
        Assert.Equal(AlertLevel.Critical, second.Current);
        Assert.True(second.SendAlert);
    }

    [Fact]
    public void WarningThenCritical_SendsTwoAlerts()
    {
        var run = new StateRun();

        run.Feed(85);
        var warning = run.Feed(95);
        var critical = run.Feed(95);

        Assert.Equal(AlertLevel.Warning, warning.Current);
        Assert.True(warning.SendAlert);
        Assert.Equal(AlertLevel.Critical, critical.Current);
        Assert.True(critical.SendAlert);
    }

    // --- Falling and hysteresis ---------------------------------------------

    [Fact]
    public void Critical_HoldsWithinHysteresis_ThenFallsToWarningSilently()
    {
        var run = new StateRun();
        run.FeedTwice(95);

        var held = run.Feed(88);     // 88 >= 90 - 3
        var fallen = run.Feed(86);   // 86 < 87 but still above Warning

        Assert.Equal(AlertLevel.Critical, held.Current);
        Assert.Equal(AlertLevel.Warning, fallen.Current);
        Assert.False(fallen.SendAlert);
        Assert.False(fallen.SendRecovered);
    }

    [Fact]
    public void Warning_HoldsWithinHysteresis_ThenFallsToNormal()
    {
        var run = new StateRun();
        run.FeedTwice(85);

        var held = run.Feed(78);     // 78 >= 80 - 3
        var fallen = run.Feed(76);

        Assert.Equal(AlertLevel.Warning, held.Current);
        Assert.Equal(AlertLevel.Normal, fallen.Current);
    }

    [Fact]
    public void CriticalToNormal_InOneReading_SendsRecovered()
    {
        var run = new StateRun();
        run.FeedTwice(95);

        var update = run.Feed(50);

        Assert.Equal(AlertLevel.Normal, update.Current);
        Assert.True(update.SendRecovered);
    }

    [Fact]
    public void WorkedExampleFromArchitectureDoc()
    {
        var run = new StateRun();

        run.Feed(85);
        var warning = run.Feed(95);
        var critical = run.Feed(95);
        var held = run.Feed(88);
        var warningAgain = run.Feed(86);
        var normal = run.Feed(76);

        Assert.True(warning.SendAlert);
        Assert.True(critical.SendAlert);
        Assert.Equal(AlertLevel.Critical, held.Current);
        Assert.Equal(AlertLevel.Warning, warningAgain.Current);
        Assert.False(warningAgain.SendAlert);
        Assert.Equal(AlertLevel.Normal, normal.Current);
        Assert.True(normal.SendRecovered);
    }

    // --- Bad input ----------------------------------------------------------

    [Theory]
    [InlineData(80, 80)]
    [InlineData(90, 80)]
    [InlineData(double.NaN, 90)]
    [InlineData(80, double.PositiveInfinity)]
    public void InvalidThreshold_IsRejected(double warning, double critical)
    {
        Assert.Throws<ArgumentException>(() => new ThresholdState(new Threshold(warning, critical), new MonitorOptions()));
    }

    [Theory]
    [InlineData("PollInterval")]
    [InlineData("Hysteresis")]
    [InlineData("ConfirmReadings")]
    [InlineData("Cooldown")]
    [InlineData("SustainedCriticalAfter")]
    public void InvalidOptions_AreRejected(string badSetting)
    {
        var options = badSetting switch
        {
            "PollInterval" => new MonitorOptions { PollInterval = TimeSpan.Zero },
            "Hysteresis" => new MonitorOptions { Hysteresis = -1 },
            "ConfirmReadings" => new MonitorOptions { ConfirmReadings = 0 },
            "Cooldown" => new MonitorOptions { Cooldown = TimeSpan.FromSeconds(-1) },
            "SustainedCriticalAfter" => new MonitorOptions { SustainedCriticalAfter = TimeSpan.FromSeconds(-1) },
            _ => throw new ArgumentOutOfRangeException(nameof(badSetting)),
        };

        Assert.Throws<ArgumentException>(() => new ThresholdState(new Threshold(80, 90), options));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void NonFiniteValue_IsRejected(double value)
    {
        var state = new ThresholdState(new Threshold(80, 90), new MonitorOptions());

        Assert.Throws<ArgumentOutOfRangeException>(() => state.Update(value, StateRun.Start));
    }
}

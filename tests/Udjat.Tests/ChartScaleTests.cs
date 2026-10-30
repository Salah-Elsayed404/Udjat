using Udjat.Core.Status;
using Xunit;

namespace Udjat.Tests;

/// <summary>Tests for <see cref="ChartScale"/>: the round numbers on a chart's axes.</summary>
public class ChartScaleTests
{
    [Theory]
    [InlineData(50, 54, 20)]    // a small wiggle: at least 20 degrees
    [InlineData(61, 61, 20)]    // a flat line
    [InlineData(40, 90, 20)]    // a wide range
    [InlineData(35, 36, 36)]    // the same small wiggle in Fahrenheit (36 degrees at least)
    public void Fit_GivesFourRoundStepsThatHoldTheData(double min, double max, double minSpan)
    {
        var range = ChartScale.Fit(min, max, minSpan);

        Assert.True(range.Low <= min && range.High >= max, $"[{range.Low}, {range.High}] must hold [{min}, {max}]");
        Assert.True(range.High - range.Low >= minSpan);
        Assert.Contains(Step(range), new[] { 1.0, 2.0, 2.5, 5.0, 10.0, 20.0, 25.0, 50.0, 100.0 });
        Assert.Equal(0, range.Low % Step(range), 6);   // every grid line is a multiple of the step
    }

    [Fact]
    public void Fit_ForTheCommonCases_GivesTheRangesYouWouldDrawByHand()
    {
        Assert.Equal((40.0, 60.0), ChartScale.Fit(50, 54, 20));   // the data sits in the middle
        Assert.Equal((50.0, 70.0), ChartScale.Fit(61, 61, 20));
        Assert.Equal((30.0, 70.0), ChartScale.Fit(40, 62, 20));   // 22 wide needs steps of 10; the extra room is shared top and bottom
    }

    [Fact]
    public void Fit_NeverCutsOffTheData_OnAnyRange()
    {
        for (double min = -10; min < 130; min += 3.7)
        {
            foreach (var width in new[] { 0.0, 0.5, 4, 11.3, 25, 70, 160 })
            {
                var (low, high) = ChartScale.Fit(min, min + width, 20);

                Assert.True(low <= min + 1e-9 && high >= min + width - 1e-9, $"[{low}, {high}] must hold [{min}, {min + width}]");
            }
        }
    }

    [Fact]
    public void FitFromZero_StartsAtZero_AndLeavesRoomAboveTheData()
    {
        Assert.Equal((0.0, 1000.0), ChartScale.FitFromZero(120, 1000));    // an idle fan still gets a readable scale
        Assert.Equal((0.0, 2400.0), ChartScale.FitFromZero(2062, 1000));   // steps of 600, not a jump to 4,000
        Assert.Equal((0.0, 1600.0), ChartScale.FitFromZero(1190, 1000));
    }

    [Fact]
    public void Ticks_RunFromTheTopDown_AndLabelsAreShort()
    {
        var ticks = ChartScale.Ticks(0, 100);

        Assert.Equal([100.0, 75.0, 50.0, 25.0, 0.0], ticks);
        Assert.Equal(["100", "75", "50", "25", "0"], ticks.Select(ChartScale.Label));
        Assert.Equal("62.5", ChartScale.Label(62.5));
        Assert.Equal("1,500", ChartScale.Label(1500));
        Assert.Equal("0", ChartScale.Label(-0.0));
    }

    [Theory]
    [InlineData(0, "now")]
    [InlineData(45, "-45 s")]
    [InlineData(89, "-89 s")]
    [InlineData(100, "-2 min")]
    [InlineData(900, "-15 min")]
    [InlineData(1800, "-30 min")]
    public void TheTimeAxis_SaysHowLongAgo(double seconds, string expected) =>
        Assert.Equal(expected, ChartScale.Ago(TimeSpan.FromSeconds(seconds)));

    private static double Step((double Low, double High) range) => (range.High - range.Low) / ChartScale.Intervals;
}

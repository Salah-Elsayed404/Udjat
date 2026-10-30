using System.Globalization;

namespace Udjat.Core.Status;

/// <summary>
/// The numbers on a chart's axes. A range is always four equal steps and each step is a round number, so the grid lines carry
/// labels like 40, 50, 60 instead of 43.7, 51.2. Pure, so it is tested.
/// </summary>
public static class ChartScale
{
    /// <summary>A chart has this many gaps between its grid lines, so <c>Intervals + 1</c> lines.</summary>
    public const int Intervals = 4;

    private static readonly double[] CoarseSteps = [1, 2, 2.5, 5, 10];

    // A fan's speed has room for finer steps, so 2,062 RPM is drawn on 0 to 2,400 and not on 0 to 4,000.
    private static readonly double[] FineSteps = [1, 1.2, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10];

    /// <summary>
    /// The range for a line that goes from <paramref name="min"/> to <paramref name="max"/>: round steps, at least
    /// <paramref name="minSpan"/> wide (so a small wiggle stays small), with the data in the middle where there is room.
    /// </summary>
    public static (double Low, double High) Fit(double min, double max, double minSpan)
    {
        var span = Math.Max(max - min, minSpan);
        for (var step = RoundStep(span / Intervals, CoarseSteps); ; step = RoundStep(step * 1.01, CoarseSteps))
        {
            var low = Math.Floor(min / step) * step;
            if (low + Intervals * step < max)
                continue;  // the range does not reach the top of the data: try the next round step

            var free = Intervals * step - (max - min);
            var centred = Math.Round((min - free / 2) / step) * step;
            if (centred <= min && centred + Intervals * step >= max)
                low = centred;

            return (low, low + Intervals * step);
        }
    }

    /// <summary>The range for something that starts at zero, such as a fan's speed: at least <paramref name="minHigh"/>, with a little room above the data.</summary>
    public static (double Low, double High) FitFromZero(double max, double minHigh)
    {
        var step = RoundStep(Math.Max(max * 1.05, minHigh) / Intervals, FineSteps);
        return (0, Intervals * step);
    }

    /// <summary>The values of the grid lines, from the top one down: for 0 to 100 that is 100, 75, 50, 25, 0.</summary>
    public static IReadOnlyList<double> Ticks(double low, double high) =>
        Enumerable.Range(0, Intervals + 1).Select(i => high - (high - low) * i / Intervals).ToList();

    /// <summary>A tick as text: "62.5", "100", "1,500".</summary>
    public static string Label(double value) => (value + 0.0).ToString("#,##0.#", CultureInfo.InvariantCulture);

    /// <summary>How long ago the left edge, or the middle, of the time axis is: "-30 min", "-45 s", or "now" for nothing.</summary>
    public static string Ago(TimeSpan span)
    {
        if (span < TimeSpan.FromSeconds(1))
            return "now";

        return span < TimeSpan.FromSeconds(90)
            ? $"-{Math.Round(span.TotalSeconds).ToString("0", CultureInfo.InvariantCulture)} s"
            : $"-{Math.Round(span.TotalMinutes).ToString("0", CultureInfo.InvariantCulture)} min";
    }

    /// <summary>The next round number at or above <paramref name="raw"/>: one of the multiples, times a power of ten.</summary>
    private static double RoundStep(double raw, double[] multiples)
    {
        if (raw <= 0 || double.IsNaN(raw))
            return 1;

        var power = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        foreach (var multiple in multiples)
        {
            if (raw <= multiple * power * (1 + 1e-9))
                return multiple * power;
        }

        return 10 * power;
    }
}

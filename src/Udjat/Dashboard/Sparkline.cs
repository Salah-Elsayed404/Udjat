using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Udjat.Core.Sensors;
using Udjat.Core.Status;

namespace Udjat.Dashboard;

internal enum SparkRole
{
    /// <summary>Drawn to fit its own range, so a few degrees of change can be seen.</summary>
    Temperature,

    /// <summary>A percentage, always drawn from 0 to 100.</summary>
    Load,

    /// <summary>A fan speed, drawn from 0 up to its highest point (at least 1,000 RPM): a steady fan is a steady line.</summary>
    Speed,
}

/// <summary>One line of a chart: a reading's recent history and what kind of line it is.</summary>
internal sealed record SparkSeries(SparkRole Role, IReadOnlyList<HistoryPoint> Points);

/// <summary>
/// A chart of the last 30 minutes (or of all the history there is, when that is less). Each line has its own scale (temperatures
/// fitted to their range, percentages from 0 to 100), so a panel can show temperature and load together. With
/// <see cref="ShowAxes"/> it sits on a grid and says what the lines are worth: the first line's numbers on the left in its colour,
/// the second's on the right in its colour, and the time along the bottom. Without it, only the lines.
/// </summary>
internal sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty SeriesProperty = Register(nameof(Series), typeof(IReadOnlyList<SparkSeries>), null);
    public static readonly DependencyProperty TemperatureBrushProperty = Register(nameof(TemperatureBrush), typeof(Brush), null);
    public static readonly DependencyProperty LoadBrushProperty = Register(nameof(LoadBrush), typeof(Brush), null);
    public static readonly DependencyProperty GridBrushProperty = Register(nameof(GridBrush), typeof(Brush), null);
    public static readonly DependencyProperty LabelBrushProperty = Register(nameof(LabelBrush), typeof(Brush), null);
    public static readonly DependencyProperty ShowAxesProperty = Register(nameof(ShowAxes), typeof(bool), false);
    public static readonly DependencyProperty TemperatureUnitProperty = Register(nameof(TemperatureUnit), typeof(TemperatureUnit), TemperatureUnit.Celsius);

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MinSpan = TimeSpan.FromSeconds(10);
    private static readonly Typeface LabelFace = new("Segoe UI");
    private const double Padding = 3;
    private const double LabelSize = 9.5;
    private const double LabelGap = 5;

    public IReadOnlyList<SparkSeries>? Series { get => (IReadOnlyList<SparkSeries>?)GetValue(SeriesProperty); set => SetValue(SeriesProperty, value); }
    public Brush? TemperatureBrush { get => (Brush?)GetValue(TemperatureBrushProperty); set => SetValue(TemperatureBrushProperty, value); }
    public Brush? LoadBrush { get => (Brush?)GetValue(LoadBrushProperty); set => SetValue(LoadBrushProperty, value); }
    public Brush? GridBrush { get => (Brush?)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public Brush? LabelBrush { get => (Brush?)GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }
    public bool ShowAxes { get => (bool)GetValue(ShowAxesProperty); set => SetValue(ShowAxesProperty, value); }
    public TemperatureUnit TemperatureUnit { get => (TemperatureUnit)GetValue(TemperatureUnitProperty); set => SetValue(TemperatureUnitProperty, value); }

    /// <summary>A line as it is drawn: its brush, its points as values in the unit shown, and the range it is drawn in.</summary>
    private sealed record Line(SparkSeries Source, Brush? Brush, List<(DateTimeOffset Time, double Value)> Points, double Low, double High);

    protected override void OnRender(DrawingContext dc)
    {
        var series = (Series ?? []).Where(s => s.Points.Count > 0).ToList();
        if (series.Count == 0 || ActualWidth < 20 || ActualHeight < 10)
            return;

        // The time axis fits the history there is, up to 30 minutes, so a young chart fills its width
        // instead of huddling at the right edge. A few seconds at least, so one or two points have a width.
        var end = series.Max(s => s.Points[^1].Time);
        var start = series.Min(s => s.Points[0].Time);
        if (end - start > Window)
            start = end - Window;
        var span = end - start < MinSpan ? MinSpan : end - start;

        var lines = series.Select(s => MakeLine(s, start)).Where(l => l.Points.Count > 0).ToList();
        var axes = ShowAxes && ActualWidth >= 160 && ActualHeight >= 80;
        var plot = axes ? DrawGrid(dc, lines, span) : new Rect(0, Padding, ActualWidth, ActualHeight - 2 * Padding);

        foreach (var line in lines)
            DrawLine(dc, line, plot, start, span);
    }

    private Line MakeLine(SparkSeries series, DateTimeOffset start)
    {
        var unit = TemperatureUnit;
        var points = series.Points
            .Where(p => p.Time >= start)
            .Select(p => (Time: p.Time, Value: series.Role == SparkRole.Temperature ? Temperatures.From(p.Value, unit) : p.Value))
            .ToList();
        var brush = series.Role == SparkRole.Temperature ? TemperatureBrush : LoadBrush;
        if (points.Count == 0)
            return new Line(series, brush, points, 0, 1);

        var (low, high) = series.Role switch
        {
            SparkRole.Load => (0.0, 100.0),
            SparkRole.Speed => ChartScale.FitFromZero(points.Max(p => p.Value), 1000),
            _ => ChartScale.Fit(points.Min(p => p.Value), points.Max(p => p.Value), unit == TemperatureUnit.Fahrenheit ? 36 : 20),  // 20 degrees at least
        };
        return new Line(series, brush, points, low, high);
    }

    /// <summary>
    /// Draws the grid, the numbers and the time, and returns the rectangle the lines go in. The first line's numbers are on the
    /// left and a second line's (a load next to a temperature) on the right.
    /// </summary>
    private Rect DrawGrid(DrawingContext dc, List<Line> lines, TimeSpan span)
    {
        var left = lines[0];
        var right = lines.Count > 1 ? lines[1] : null;
        var leftLabels = ChartScale.Ticks(left.Low, left.High).Select(ChartScale.Label).ToList();
        var rightLabels = right is null ? [] : ChartScale.Ticks(right.Low, right.High).Select(ChartScale.Label).ToList();

        var leftWidth = leftLabels.Max(t => Text(t, left.Brush).Width) + LabelGap;
        var rightWidth = right is null ? 0 : rightLabels.Max(t => Text(t, right.Brush).Width) + LabelGap;
        var plot = new Rect(
            Padding + leftWidth, Padding + LabelSize / 2,
            ActualWidth - 2 * Padding - leftWidth - rightWidth, ActualHeight - 2 * Padding - LabelSize / 2 - LabelSize - 4);

        var grid = new Pen(GridBrush, 1);
        for (var i = 0; i <= ChartScale.Intervals; i++)
        {
            var y = Math.Round(plot.Top + plot.Height * i / ChartScale.Intervals) + 0.5;  // on a pixel, so the line is crisp
            var x = Math.Round(plot.Left + plot.Width * i / ChartScale.Intervals) + 0.5;
            dc.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
            dc.DrawLine(grid, new Point(x, plot.Top), new Point(x, plot.Bottom));

            var leftText = Text(leftLabels[i], left.Brush);
            dc.DrawText(leftText, new Point(plot.Left - LabelGap - leftText.Width, y - leftText.Height / 2));
            if (right is not null)
                dc.DrawText(Text(rightLabels[i], right.Brush), new Point(plot.Right + LabelGap, y - leftText.Height / 2));
        }

        // The time: the left edge is the oldest point, the right edge is now.
        var ago = new[] { ChartScale.Ago(span), ChartScale.Ago(span / 2), ChartScale.Ago(TimeSpan.Zero) };
        for (var i = 0; i < ago.Length; i++)
        {
            var text = Text(ago[i], LabelBrush);
            var x = plot.Left + plot.Width * i / 2 - text.Width * i / 2;  // left-aligned, centred, right-aligned
            dc.DrawText(text, new Point(x, plot.Bottom + 3));
        }

        return plot;
    }

    private static void DrawLine(DrawingContext dc, Line line, Rect plot, DateTimeOffset start, TimeSpan span)
    {
        var screen = line.Points
            .Select(p => new Point(
                plot.Left + plot.Width * (p.Time - start) / span,
                plot.Top + plot.Height * (1 - (p.Value - line.Low) / (line.High - line.Low))))
            .ToList();

        if (screen.Count > 1)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(screen[0], isFilled: false, isClosed: false);
                context.PolyLineTo(screen.Skip(1).ToList(), isStroked: true, isSmoothJoin: true);
            }

            geometry.Freeze();
            dc.DrawGeometry(null, new Pen(line.Brush, 1.6) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geometry);
        }

        dc.DrawEllipse(line.Brush, null, screen[^1], 2.6, 2.6);  // the latest value (the only mark when there is one point)
    }

    private FormattedText Text(string text, Brush? brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelFace, LabelSize, brush ?? Brushes.Gray, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static DependencyProperty Register(string name, Type type, object? defaultValue) =>
        DependencyProperty.Register(name, type, typeof(Sparkline), new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));
}

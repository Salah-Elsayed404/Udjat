using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Udjat.Dashboard;

/// <summary>A ring that fills clockwise from the top to show a percentage, with the figure in the middle.</summary>
internal sealed class RingGauge : FrameworkElement
{
    /// <summary>0 to 100. NaN (no reading) draws only the empty ring.</summary>
    public static readonly DependencyProperty ValueProperty = Register(nameof(Value), typeof(double), double.NaN);
    public static readonly DependencyProperty TextProperty = Register(nameof(Text), typeof(string), "");
    public static readonly DependencyProperty TrackBrushProperty = Register(nameof(TrackBrush), typeof(Brush), null);
    public static readonly DependencyProperty ArcBrushProperty = Register(nameof(ArcBrush), typeof(Brush), null);
    public static readonly DependencyProperty TextBrushProperty = Register(nameof(TextBrush), typeof(Brush), null);

    private const double Thickness = 7;
    private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public Brush? TrackBrush { get => (Brush?)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public Brush? ArcBrush { get => (Brush?)GetValue(ArcBrushProperty); set => SetValue(ArcBrushProperty, value); }
    public Brush? TextBrush { get => (Brush?)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size < 20)
            return;

        var centre = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = (size - Thickness) / 2;

        dc.DrawEllipse(null, new Pen(TrackBrush, Thickness), centre, radius, radius);

        if (!double.IsNaN(Value) && Value > 0)
            DrawArc(dc, centre, radius, Math.Min(Value, 100));

        var text = new FormattedText(
            Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 15, TextBrush ?? Brushes.Gray,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point(centre.X - text.Width / 2, centre.Y - text.Height / 2));
    }

    /// <summary>The filled part of the ring: from the top, clockwise, round at both ends.</summary>
    private void DrawArc(DrawingContext dc, Point centre, double radius, double percent)
    {
        var pen = new Pen(ArcBrush, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

        if (percent >= 99.9)
        {
            dc.DrawEllipse(null, pen, centre, radius, radius);  // an arc cannot start and end at the same point
            return;
        }

        var angle = percent / 100 * 2 * Math.PI;
        var start = new Point(centre.X, centre.Y - radius);
        var end = new Point(centre.X + radius * Math.Sin(angle), centre.Y - radius * Math.Cos(angle));

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, isFilled: false, isClosed: false);
            context.ArcTo(end, new Size(radius, radius), 0, isLargeArc: percent > 50, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private static DependencyProperty Register(string name, Type type, object? defaultValue) =>
        DependencyProperty.Register(name, type, typeof(RingGauge), new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));
}

using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Udjat.Core.Status;
using Wpf = System.Windows.Media;

namespace Udjat.Tray;

/// <summary>
/// Draws the tray icons: the Eye of Horus in the status colour on a dark disc. The eye is the one from Themes/Shared.xaml, drawn
/// as a solid shape with a thin outline so its thin parts survive at 16 pixels; the neon depth of the program icon would only blur.
/// </summary>
internal static class TrayIcons
{
    private static readonly Wpf.Color Night = Wpf.Color.FromRgb(11, 14, 19);
    private static readonly Wpf.Color Green = Wpf.Color.FromRgb(63, 185, 80);
    private static readonly Wpf.Color Amber = Wpf.Color.FromRgb(227, 179, 65);
    private static readonly Wpf.Color Red = Wpf.Color.FromRgb(248, 81, 73);
    private static readonly Wpf.Color Grey = Wpf.Color.FromRgb(125, 133, 144);

    /// <summary>The eye's box is 120 x 96; this is how much of the icon it fills.</summary>
    private const double EyeScale = 0.86;

    public static Icon Create(TrayState state)
    {
        var size = System.Windows.Forms.SystemInformation.SmallIconSize.Width;  // follows the screen's DPI scaling

        using var stream = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Draw(size, ColourOf(state))));
        encoder.Save(stream);
        stream.Position = 0;

        // GetHicon makes a handle that must be freed by hand; the clone is an icon that owns its own copy.
        using var bitmap = new Bitmap(stream);
        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static RenderTargetBitmap Draw(int size, Wpf.Color colour)
    {
        var resources = System.Windows.Application.Current.Resources;
        var brush = new SolidColorBrush(colour);
        var scale = size * EyeScale / 120;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawEllipse(new SolidColorBrush(Night), null, new System.Windows.Point(size / 2.0, size / 2.0), size / 2.0, size / 2.0);

            dc.PushTransform(new TranslateTransform((size - 120 * scale) / 2, (size - 96 * scale) / 2));
            dc.PushTransform(new ScaleTransform(scale, scale));
            var round = new Wpf.Pen(brush, 7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            dc.DrawGeometry(null, round, (Geometry)resources["EyeSpiral"]);
            dc.DrawGeometry(brush, null, (Geometry)resources["EyePupil"]);
            dc.DrawGeometry(brush, new Wpf.Pen(brush, 3) { LineJoin = PenLineJoin.Round }, (Geometry)resources["EyeShapes"]);
            dc.Pop();
            dc.Pop();
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    private static Wpf.Color ColourOf(TrayState state) => state switch
    {
        TrayState.Normal => Green,
        TrayState.Warning => Amber,
        TrayState.Critical => Red,
        _ => Grey,
    };

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool DestroyIcon(IntPtr handle);
}

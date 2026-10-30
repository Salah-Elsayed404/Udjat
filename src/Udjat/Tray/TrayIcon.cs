using System.Windows.Forms;
using Udjat.Core.Status;

namespace Udjat.Tray;

/// <summary>
/// The icon in the notification area: its colour and tooltip show the status, its menu has the main actions,
/// and its balloon tips are Udjat's Windows notifications. Use from the UI thread only.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    // The limits Windows puts on a balloon tip.
    private const int MaxTitle = 63;
    private const int MaxText = 255;

    private readonly NotifyIcon _icon;
    private readonly System.Drawing.Icon[] _icons;

    /// <param name="menu">The menu entries, top to bottom. A null action is a separator.</param>
    /// <param name="onDoubleClick">What a double click does (open the dashboard).</param>
    public TrayIcon(IEnumerable<(string Text, Action? Click)> menu, Action onDoubleClick)
    {
        _icons = Enum.GetValues<TrayState>().Select(TrayIcons.Create).ToArray();

        var strip = new ContextMenuStrip();
        foreach (var (text, click) in menu)
        {
            if (click is null)
                strip.Items.Add(new ToolStripSeparator());
            else
                strip.Items.Add(text, null, (_, _) => click());
        }

        _icon = new NotifyIcon { Icon = _icons[(int)TrayState.NotMonitoring], Text = "Udjat", ContextMenuStrip = strip };
        _icon.DoubleClick += (_, _) => onDoubleClick();
    }

    public void Show() => _icon.Visible = true;

    /// <summary>Sets the colour and the tooltip.</summary>
    public void SetStatus(StatusSummary status)
    {
        _icon.Icon = _icons[(int)status.State];
        _icon.Text = status.Text;  // StatusSummary already keeps this within the 127 characters Windows allows
    }

    /// <summary>Shows a Windows notification (a balloon tip, which Windows 10 and 11 present as a toast).</summary>
    public void ShowBalloon(string title, string text, ToolTipIcon icon)
    {
        _icon.ShowBalloonTip(10_000, Cut(title, MaxTitle), Cut(text, MaxText), icon);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        foreach (var icon in _icons)
            icon.Dispose();
    }

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}

using System.Windows.Forms;
using System.Windows.Threading;
using Udjat.Core.Alerts;
using Udjat.Core.Monitoring;
using Udjat.Tray;

namespace Udjat.Channels;

/// <summary>
/// The Windows notification. It is a balloon tip from the tray icon, which Windows 10 and 11 shows as a toast.
/// "Focus assist" can hide it, which is one reason a Critical alert also sounds an alarm.
/// </summary>
internal sealed class ToastChannel(TrayIcon tray, Dispatcher ui) : IAlertChannel
{
    public ChannelKind Kind => ChannelKind.Toast;

    public Task SendAsync(AlertMessage message, CancellationToken cancellationToken)
    {
        var icon = message.Level switch
        {
            AlertLevel.Critical => ToolTipIcon.Error,
            AlertLevel.Warning => ToolTipIcon.Warning,
            _ => ToolTipIcon.Info,
        };

        // The tray icon belongs to the UI thread; this runs on the dispatcher's background thread.
        return ui.InvokeAsync(() => tray.ShowBalloon(message.Title, message.Body, icon), DispatcherPriority.Normal, cancellationToken).Task;
    }
}

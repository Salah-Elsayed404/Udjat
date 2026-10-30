using System.Media;
using Udjat.Core.Alerts;
using Udjat.Core.Monitoring;

namespace Udjat.Channels;

/// <summary>
/// The local alarm: Windows' own Alarm01.wav (so there is nothing to ship or license), played once for a Warning
/// and three times for Critical. A PC with its sound muted stays silent.
/// </summary>
internal sealed class SoundChannel : IAlertChannel
{
    private static readonly string AlarmFile =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Alarm01.wav");

    public ChannelKind Kind => ChannelKind.Sound;

    public Task SendAsync(AlertMessage message, CancellationToken cancellationToken)
    {
        var times = message.Level == AlertLevel.Critical ? 3 : 1;
        return Task.Run(() => Play(times, cancellationToken), cancellationToken);
    }

    private static void Play(int times, CancellationToken cancellationToken)
    {
        if (!File.Exists(AlarmFile))
        {
            SystemSounds.Hand.Play();  // the system's own error sound, in case the alarm file was removed
            return;
        }

        for (var i = 0; i < times; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var player = new SoundPlayer(AlarmFile);
            player.PlaySync();
        }
    }
}

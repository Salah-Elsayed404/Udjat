using Udjat.Core.Monitoring;
using Udjat.Core.Settings;

namespace Udjat.Core.Alerts;

/// <summary>Decides which channels get a message. The rules are in docs/ALERTS.md.</summary>
public static class ChannelRouter
{
    /// <summary>
    /// The channels for this message, in a fixed order, without repeats.
    /// A channel is used only if it is switched on. Sound is an alarm, so it is only for Warning and Critical alerts.
    /// </summary>
    /// <param name="heardInEpisode">
    /// For a "recovered" message: the channels that got the episode's highest alert,
    /// so the news that it is over reaches the same people.
    /// </param>
    public static IReadOnlyList<ChannelKind> For(
        AppSettings settings,
        AlertMessage message,
        IReadOnlyCollection<ChannelKind>? heardInEpisode = null)
    {
        IEnumerable<ChannelKind> wanted = message.Kind switch
        {
            AlertMessageKind.Alert => message.Level == AlertLevel.Critical ? settings.Routing.Critical : settings.Routing.Warning,
            AlertMessageKind.Recovered => heardInEpisode ?? [],
            AlertMessageKind.AutoProtect or AlertMessageKind.MonitoringStopped or AlertMessageKind.MonitoringResumed => settings.Routing.Critical,
            _ => [],  // a test message goes straight to the one channel being tested
        };

        return Enum.GetValues<ChannelKind>()
            .Where(channel => wanted.Contains(channel)
                              && IsEnabled(settings, channel)
                              && (channel != ChannelKind.Sound || message.Kind == AlertMessageKind.Alert))
            .ToList();
    }

    public static bool IsEnabled(AppSettings settings, ChannelKind channel) => channel switch
    {
        ChannelKind.Toast => settings.Toast.Enabled,
        ChannelKind.Sound => settings.Sound.Enabled,
        ChannelKind.Email => settings.Email.Enabled,
        ChannelKind.Telegram => settings.Telegram.Enabled,
        _ => false,
    };
}

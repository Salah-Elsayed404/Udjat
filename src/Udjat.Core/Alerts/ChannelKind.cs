namespace Udjat.Core.Alerts;

/// <summary>The ways Udjat can tell the user about something. Each one is optional.</summary>
public enum ChannelKind
{
    Toast,
    Sound,
    Email,
    Telegram,
}

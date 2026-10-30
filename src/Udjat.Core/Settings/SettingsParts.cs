using Udjat.Core.Alerts;

namespace Udjat.Core.Settings;

// The pieces of AppSettings. Each one has the defaults from docs/ALERTS.md, so a missing field in the file takes them.

/// <summary>The limits for one kind of sensor. A disabled kind is shown but never alerts.</summary>
public sealed record ThresholdSetting(bool Enabled, double Warning, double Critical);

/// <summary>Which channels hear about each level. A channel also has to be enabled to be used.</summary>
public sealed record RoutingSettings
{
    public List<ChannelKind> Warning { get; init; } = [ChannelKind.Toast];

    public List<ChannelKind> Critical { get; init; } =
        [ChannelKind.Toast, ChannelKind.Sound, ChannelKind.Email, ChannelKind.Telegram];
}

/// <summary>A channel with nothing to set up except on or off (toast and sound).</summary>
public sealed record ChannelToggle
{
    public bool Enabled { get; init; } = true;
}

/// <summary>How the connection to the mail server is protected. There is deliberately no plain-text option.</summary>
public enum MailSecurity
{
    StartTls,
    SslOnConnect,
}

/// <summary>Email settings. The password is not here: it lives in Windows Credential Manager.</summary>
public sealed record EmailSettings
{
    public bool Enabled { get; init; }
    public string Host { get; init; } = "";
    public int Port { get; init; } = 587;
    public MailSecurity Security { get; init; } = MailSecurity.StartTls;
    public string Username { get; init; } = "";
    public string From { get; init; } = "";
    public string To { get; init; } = "";
}

/// <summary>Telegram settings. The bot token is not here: it lives in Windows Credential Manager.</summary>
public sealed record TelegramSettings
{
    public bool Enabled { get; init; }

    /// <summary>A whole number; group chats are negative. Kept as text so an empty field is not a zero.</summary>
    public string ChatId { get; init; } = "";
}

/// <summary>Closing the user's chosen programs when a sensor stays Critical. Off by default.</summary>
public sealed record AutoProtectSettings
{
    public bool Enabled { get; init; }
    public double AfterSeconds { get; init; } = 60;
    public List<string> ProcessNames { get; init; } = [];
}

/// <summary>The look of the dashboard.</summary>
public enum AppTheme
{
    Dark,
    Light,
}

/// <summary>Settings for people who edit the file by hand. Not shown in the settings window.</summary>
public sealed record AdvancedSettings
{
    public double PollSeconds { get; init; } = 2;
    public double Hysteresis { get; init; } = 3;
    public int ConfirmReadings { get; init; } = 2;
}

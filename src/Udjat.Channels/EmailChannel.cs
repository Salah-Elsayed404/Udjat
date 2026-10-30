using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Udjat.Core.Alerts;
using Udjat.Core.Settings;

namespace Udjat.Channels;

/// <summary>
/// Sends a plain-text email through the user's own SMTP server. The connection is always encrypted:
/// the only options are STARTTLS (which is required, not optional) or TLS from the first byte, and the
/// server's certificate is checked. The password is a secret that comes from Credential Manager.
/// </summary>
public sealed class EmailChannel : IAlertChannel
{
    private readonly EmailSettings _settings;
    private readonly Func<MimeMessage, CancellationToken, Task> _send;

    /// <summary>The real channel: sends through MailKit.</summary>
    public EmailChannel(EmailSettings settings, string password)
        : this(settings, (message, token) => SendWithMailKitAsync(settings, password, message, token))
    {
    }

    /// <summary>For tests and other transports: the message is built here and handed to <paramref name="send"/>.</summary>
    public EmailChannel(EmailSettings settings, Func<MimeMessage, CancellationToken, Task> send)
    {
        _settings = settings;
        _send = send;
    }

    public ChannelKind Kind => ChannelKind.Email;

    public Task SendAsync(AlertMessage message, CancellationToken cancellationToken) =>
        _send(BuildMessage(_settings, message), cancellationToken);

    /// <summary>The email for an alert: the title as the subject, the details as plain text.</summary>
    public static MimeMessage BuildMessage(EmailSettings settings, AlertMessage alert)
    {
        var email = new MimeMessage();
        email.From.Add(MailboxAddress.Parse(settings.From));
        email.To.Add(MailboxAddress.Parse(settings.To));
        email.Subject = alert.Title;
        email.Body = new TextPart("plain") { Text = alert.Body };
        return email;
    }

    private static async Task SendWithMailKitAsync(EmailSettings settings, string password, MimeMessage message, CancellationToken cancellationToken)
    {
        // StartTls (not "when available"): if the server cannot encrypt, nothing is sent.
        var security = settings.Security == MailSecurity.SslOnConnect ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;

        using var client = new SmtpClient();
        await client.ConnectAsync(settings.Host, settings.Port, security, cancellationToken);

        if (!string.IsNullOrEmpty(settings.Username))
            await client.AuthenticateAsync(settings.Username, password, cancellationToken);

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}

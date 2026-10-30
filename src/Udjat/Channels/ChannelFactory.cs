using System.ComponentModel;
using System.Windows.Threading;
using Udjat.Core.Alerts;
using Udjat.Core.Settings;
using Udjat.Tray;
using Udjat.Windows.Secrets;

namespace Udjat.Channels;

/// <summary>The channels to use, the secrets in them (so they can be hidden from logs), and what is missing.</summary>
internal sealed record BuiltChannels(
    IReadOnlyList<IAlertChannel> Channels,
    IReadOnlyList<string> Secrets,
    bool EmailMissingPassword,
    bool TelegramMissingToken);

/// <summary>
/// Builds the channels from the settings. Toast and sound are always there; email and Telegram when they are switched on
/// and their secret is available. A channel that is on but has no usable secret is left out, and the dashboard says why.
/// </summary>
internal sealed class ChannelFactory(SecretStore secrets, HttpClient http)
{
    /// <param name="readSecret">Where to get a secret from. Normally Credential Manager; the settings page's Test buttons pass what is typed on screen.</param>
    public BuiltChannels Build(AppSettings settings, TrayIcon tray, Dispatcher ui, Func<SecretName, string?>? readSecret = null)
    {
        readSecret ??= Read;
        List<IAlertChannel> channels = [new ToastChannel(tray, ui), new SoundChannel()];
        List<string> secretValues = [];
        var emailMissing = false;
        var telegramMissing = false;

        if (settings.Email.Enabled)
        {
            var password = readSecret(SecretName.SmtpPassword);
            if (password is null && settings.Email.Username.Length > 0)
            {
                emailMissing = true;  // a server that needs a login, and no password saved
            }
            else
            {
                channels.Add(new EmailChannel(settings.Email, password ?? ""));
                if (password is not null)
                    secretValues.Add(password);
            }
        }

        if (settings.Telegram.Enabled)
        {
            var token = readSecret(SecretName.TelegramBotToken);
            if (token is not null)
            {
                try
                {
                    channels.Add(new TelegramChannel(http, token, settings.Telegram.ChatId));
                    secretValues.Add(token);
                }
                catch (ArgumentException)
                {
                    token = null;  // saved, but not a usable bot token
                }
            }

            telegramMissing = token is null;
        }

        return new BuiltChannels(channels, secretValues, emailMissing, telegramMissing);
    }

    private string? Read(SecretName name)
    {
        try
        {
            return secrets.Read(name);
        }
        catch (Win32Exception)
        {
            return null;  // treated as missing; the dashboard tells the user to enter it again
        }
    }
}

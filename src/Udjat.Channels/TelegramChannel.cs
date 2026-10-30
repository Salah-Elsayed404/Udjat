using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Udjat.Core.Alerts;

namespace Udjat.Channels;

/// <summary>
/// Sends a message to a Telegram chat through the Bot API, over HTTPS to api.telegram.org only.
/// The bot token is a secret: it is checked, kept private, and never put in an exception message
/// (the dispatcher also hides it from logs).
/// </summary>
public sealed partial class TelegramChannel : IAlertChannel
{
    private const int MaxDescriptionLength = 100;

    private readonly HttpClient _http;
    private readonly string _token;
    private readonly long _chatId;

    /// <param name="http">Made by the app, with a timeout. Tests pass one with a fake handler.</param>
    /// <param name="botToken">From BotFather, for example "123456789:AAH...". Comes from Credential Manager.</param>
    /// <param name="chatId">A whole number; group chats are negative.</param>
    /// <exception cref="ArgumentException">The token or the chat id is not in the expected form.</exception>
    public TelegramChannel(HttpClient http, string botToken, string chatId)
    {
        // The token goes into a URL path, so anything unexpected is refused. The message must not repeat it.
        if (!TokenPattern().IsMatch(botToken))
            throw new ArgumentException("The Telegram bot token is not in the expected form (digits, a colon, then letters and digits).");
        if (!long.TryParse(chatId, NumberStyles.Integer, CultureInfo.InvariantCulture, out _chatId))
            throw new ArgumentException("The Telegram chat id must be a whole number.");

        _http = http;
        _token = botToken;
    }

    public ChannelKind Kind => ChannelKind.Telegram;

    public async Task SendAsync(AlertMessage message, CancellationToken cancellationToken)
    {
        var url = $"https://api.telegram.org/bot{_token}/sendMessage";
        var payload = new { chat_id = _chatId, text = $"{message.Title}\n\n{message.Body}" };

        using var response = await _http.PostAsJsonAsync(url, payload, cancellationToken);
        if (response.IsSuccessStatusCode)
            return;

        var reason = Description(await response.Content.ReadAsStringAsync(cancellationToken));
        throw new HttpRequestException($"Telegram answered {(int)response.StatusCode}" + (reason is null ? "" : $": {reason}"));
    }

    /// <summary>Telegram explains errors in a "description" field, for example "Bad Request: chat not found".</summary>
    private static string? Description(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("description", out var field)
                || field.ValueKind != JsonValueKind.String)
                return null;

            var text = field.GetString() ?? "";
            return text.Length <= MaxDescriptionLength ? text : text[..MaxDescriptionLength] + "…";
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^\d+:[A-Za-z0-9_-]+$")]
    private static partial Regex TokenPattern();
}

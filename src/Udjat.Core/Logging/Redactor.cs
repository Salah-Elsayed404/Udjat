using System.Text.RegularExpressions;

namespace Udjat.Core.Logging;

/// <summary>
/// Hides secrets in text before it reaches the log or the screen. Error messages can contain
/// more than their authors expect, so every one passes through here.
/// </summary>
public static partial class Redactor
{
    public const string Mask = "***";

    /// <summary>
    /// Replaces every known secret with <see cref="Mask"/>. Anything shaped like a Telegram bot token
    /// is hidden too, even if it is not among the known secrets.
    /// </summary>
    public static string Redact(string text, IEnumerable<string> secrets)
    {
        // Longest first, so a secret that contains a shorter one is hidden whole.
        foreach (var secret in secrets.Where(s => !string.IsNullOrWhiteSpace(s)).OrderByDescending(s => s.Length))
            text = text.Replace(secret, Mask, StringComparison.Ordinal);

        return TelegramToken().Replace(text, Mask);
    }

    [GeneratedRegex(@"\d{6,}:[A-Za-z0-9_-]{20,}")]
    private static partial Regex TelegramToken();
}

namespace Udjat.Core.Protection;

/// <summary>
/// The rules for a process name on the auto-protect allow-list: a plain name such as "ollama",
/// with or without ".exe". Anything that could point at a path or match many processes is refused.
/// </summary>
public static class ProcessNames
{
    private static readonly char[] Forbidden = ['/', '\\', '*', '?', ':', '"', '<', '>', '|'];

    /// <summary>Trims the text and drops a trailing ".exe". Returns false if nothing usable is left.</summary>
    public static bool TryNormalize(string? raw, out string name)
    {
        name = "";
        var text = raw?.Trim() ?? "";

        if (text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            text = text[..^4].TrimEnd();

        if (text.Length == 0 || text.IndexOfAny(Forbidden) >= 0 || text.Any(char.IsControl))
            return false;

        name = text;
        return true;
    }
}

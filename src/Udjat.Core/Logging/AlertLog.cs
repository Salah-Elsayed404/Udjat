using System.Globalization;
using System.Text;

namespace Udjat.Core.Logging;

/// <summary>
/// The alert log: plain text, one line per message, readable by anyone. Only alerts, recoveries,
/// auto-protect actions and "monitoring stopped/resumed" go in; there is no sensor history.
/// When the file gets too big it becomes alerts.old.log and a new one starts.
/// </summary>
public sealed class AlertLog(string path, long maxBytes = 1_000_000)
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _path = Path.GetFullPath(path);
    private readonly object _gate = new();

    /// <summary>
    /// Adds a line: the local time, then the non-empty fields separated by " | ".
    /// Returns false if the file could not be written. A failing log never stops an alert.
    /// </summary>
    public bool Append(DateTimeOffset time, params string[] fields)
    {
        var stamp = time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var text = string.Join(" | ", fields.Where(f => !string.IsNullOrWhiteSpace(f)).Select(OneLine));
        var line = $"{stamp} | {text}{Environment.NewLine}";

        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                RotateIfFull();
                File.AppendAllText(_path, line, Utf8);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    private void RotateIfFull()
    {
        if (File.Exists(_path) && new FileInfo(_path).Length >= maxBytes)
            File.Move(_path, Path.ChangeExtension(_path, ".old.log"), overwrite: true);
    }

    /// <summary>A line break inside a field would split one entry into two.</summary>
    private static string OneLine(string text) => text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Trim();
}

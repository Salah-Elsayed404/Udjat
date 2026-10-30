using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Udjat.Core.Settings;

/// <summary>What <see cref="SettingsStore.Load"/> found.</summary>
/// <param name="Settings">The settings to use.</param>
/// <param name="Notice">Set when the file was unusable and the defaults are used instead.</param>
/// <param name="IsNew">True when there was no settings file at all: this is the first run.</param>
public sealed record LoadResult(AppSettings Settings, string? Notice, bool IsNew = false);

/// <summary>
/// Reads and writes the settings file. A missing field takes its default, an unknown field is ignored,
/// and a file that cannot be used is kept aside (settings.invalid.json) so Udjat still starts.
/// </summary>
public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string _path = Path.GetFullPath(path);

    public LoadResult Load()
    {
        if (!File.Exists(_path))
        {
            var defaults = new AppSettings();
            Save(defaults);
            return new LoadResult(defaults, null, IsNew: true);
        }

        try
        {
            return new LoadResult(Parse(File.ReadAllText(_path)), null);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or FormatException)
        {
            return UseDefaultsInstead(ex);
        }
    }

    /// <summary>Checks the settings, writes them to a temporary file, then moves it over the old one so a crash never leaves half a file.</summary>
    /// <exception cref="ArgumentException">The settings are not valid. The old file is left alone.</exception>
    public void Save(AppSettings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options), new UTF8Encoding(false));
        File.Move(temp, _path, overwrite: true);
    }

    /// <summary>The user's JSON laid over the defaults, so every field that is missing keeps its default.</summary>
    private static AppSettings Parse(string json)
    {
        var user = JsonNode.Parse(json, documentOptions: ReadOptions) as JsonObject
                   ?? throw new JsonException("The settings file must contain a JSON object.");

        var merged = (JsonObject)JsonSerializer.SerializeToNode(new AppSettings(), Options)!;
        Merge(merged, user);

        var settings = merged.Deserialize<AppSettings>(Options) ?? throw new JsonException("The settings file is empty.");
        settings.Validate();
        return settings;
    }

    private static void Merge(JsonObject target, JsonObject source)
    {
        foreach (var (key, value) in source)
        {
            if (value is JsonObject sourceChild && target[key] is JsonObject targetChild)
                Merge(targetChild, sourceChild);
            else
                target[key] = value?.DeepClone();
        }
    }

    private LoadResult UseDefaultsInstead(Exception reason)
    {
        var invalidPath = Path.ChangeExtension(_path, ".invalid.json");
        File.Move(_path, invalidPath, overwrite: true);

        var defaults = new AppSettings();
        Save(defaults);

        var notice = $"The settings file could not be used ({reason.Message}). " +
                     $"It was kept as {Path.GetFileName(invalidPath)} and the default settings are used.";
        return new LoadResult(defaults, notice);
    }
}

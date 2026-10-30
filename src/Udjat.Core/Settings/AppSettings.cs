using System.Globalization;
using System.Net.Mail;
using Udjat.Core.Alerts;
using Udjat.Core.Monitoring;
using Udjat.Core.Protection;
using Udjat.Core.Sensors;

namespace Udjat.Core.Settings;

/// <summary>
/// Everything the user can set, with the defaults. Saved as JSON by <see cref="SettingsStore"/>.
/// There are no secrets in here. See docs/ALERTS.md for the reasons behind the default values.
/// </summary>
public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public Dictionary<SensorKind, ThresholdSetting> Thresholds { get; init; } = DefaultThresholds();
    public RoutingSettings Routing { get; init; } = new();
    public ChannelToggle Toast { get; init; } = new();
    public ChannelToggle Sound { get; init; } = new();
    public EmailSettings Email { get; init; } = new();
    public TelegramSettings Telegram { get; init; } = new();
    public int CooldownMinutes { get; init; } = 10;
    public AutoProtectSettings AutoProtect { get; init; } = new();
    public bool Autostart { get; init; } = true;

    /// <summary>Dark by default; the dashboard's header button switches it.</summary>
    public AppTheme Theme { get; init; } = AppTheme.Dark;

    /// <summary>Celsius or Fahrenheit, for everything the user sees. The limits and readings are stored in Celsius either way.</summary>
    public TemperatureUnit TemperatureUnit { get; init; } = TemperatureUnit.Celsius;

    public AdvancedSettings Advanced { get; init; } = new();

    /// <summary>Ids of the dashboard notices the user dismissed for good (see <see cref="Status.Notices"/>).</summary>
    public List<string> DismissedNotices { get; init; } = [];

    /// <summary>
    /// Starting points chosen so a normal heavy AI load stays quiet.
    /// Fans have no entry: they are shown, never alerted.
    /// </summary>
    public static Dictionary<SensorKind, ThresholdSetting> DefaultThresholds() => new()
    {
        [SensorKind.CpuTemp] = new(true, 90, 97),
        [SensorKind.GpuTemp] = new(true, 84, 90),
        [SensorKind.GpuHotspot] = new(true, 100, 108),
        [SensorKind.GpuVram] = new(false, 95, 99),  // full VRAM is normal for local AI, so off by default
        [SensorKind.Ram] = new(true, 97, 99),
        [SensorKind.DriveTemp] = new(true, 50, 55),
        [SensorKind.NvmeTemp] = new(true, 75, 82),
    };

    /// <summary>The thresholds the monitor needs: enabled kinds only, and never the display-only ones (fans, load).</summary>
    public IReadOnlyDictionary<SensorKind, Threshold> ToThresholds() =>
        Thresholds
            .Where(pair => pair.Value.Enabled && !SensorKindInfo.IsDisplayOnly(pair.Key))
            .ToDictionary(pair => pair.Key, pair => new Threshold(pair.Value.Warning, pair.Value.Critical));

    public MonitorOptions ToMonitorOptions() => new()
    {
        PollInterval = TimeSpan.FromSeconds(Advanced.PollSeconds),
        Hysteresis = Advanced.Hysteresis,
        ConfirmReadings = Advanced.ConfirmReadings,
        Cooldown = TimeSpan.FromMinutes(CooldownMinutes),
        SustainedCriticalAfter = TimeSpan.FromSeconds(AutoProtect.AfterSeconds),
    };

    /// <summary>Throws <see cref="ArgumentException"/> with a message the user can act on if any setting is unusable.</summary>
    public void Validate()
    {
        if (SchemaVersion > CurrentSchemaVersion)
            throw new ArgumentException($"The settings are from a newer version of Udjat (schema {SchemaVersion}).");

        foreach (var (kind, setting) in Thresholds)
        {
            try
            {
                new Threshold(setting.Warning, setting.Critical).Validate();
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException($"{SensorKindInfo.Label(kind)}: {ex.Message}");
            }
        }

        try
        {
            ToMonitorOptions().Validate();
        }
        catch (OverflowException)
        {
            throw new ArgumentException("A time setting is too large.");
        }

        if (!Enum.IsDefined(Theme))
            throw new ArgumentException("Unknown theme.");
        if (!Enum.IsDefined(TemperatureUnit))
            throw new ArgumentException("Unknown temperature unit.");

        foreach (var channel in Routing.Warning.Concat(Routing.Critical))
        {
            if (!Enum.IsDefined(channel))
                throw new ArgumentException($"Unknown channel in the routing: {channel}.");
        }

        if (Email.Enabled)
            ValidateEmail();

        if (Telegram.Enabled && !long.TryParse(Telegram.ChatId, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            throw new ArgumentException("The Telegram chat id must be a whole number.");

        foreach (var name in AutoProtect.ProcessNames)
        {
            if (!ProcessNames.TryNormalize(name, out _))
                throw new ArgumentException($"\"{name}\" is not a valid process name. Use a plain name such as ollama.");
        }
    }

    private void ValidateEmail()
    {
        if (string.IsNullOrWhiteSpace(Email.Host))
            throw new ArgumentException("The email server (host) is empty.");
        if (Email.Port is < 1 or > 65535)
            throw new ArgumentException("The email port must be between 1 and 65535.");
        if (!Enum.IsDefined(Email.Security))
            throw new ArgumentException("Unknown email security setting.");
        if (!MailAddress.TryCreate(Email.From, out _))
            throw new ArgumentException("The email 'from' address is not valid.");
        if (!MailAddress.TryCreate(Email.To, out _))
            throw new ArgumentException("The email 'to' address is not valid.");
    }
}

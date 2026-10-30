using System.Globalization;
using Udjat.Core.Alerts;
using Udjat.Core.Sensors;

namespace Udjat.Core.Settings;

/// <summary>One row of the "Alerts" page: the limits of one kind of sensor, as text boxes.</summary>
public sealed class ThresholdForm
{
    public SensorKind Kind { get; init; }
    public string Label { get; init; } = "";
    public string Unit { get; init; } = "";
    public bool Enabled { get; set; }
    public string Warning { get; set; } = "";
    public string Critical { get; set; } = "";

    /// <summary>The same row with its limits shown in another temperature unit. A box that is not a number is left as typed.</summary>
    public ThresholdForm Converted(TemperatureUnit from, TemperatureUnit to) => new()
    {
        Kind = Kind,
        Label = Label,
        Unit = SensorKindInfo.Unit(Kind, to),
        Enabled = Enabled,
        Warning = Convert(Warning, from, to),
        Critical = Convert(Critical, from, to),
    };

    private string Convert(string text, TemperatureUnit from, TemperatureUnit to)
    {
        var typed = text.Trim().Replace(',', '.');
        if (!SensorKindInfo.IsTemperature(Kind) || !double.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            return text;

        return SettingsForm.Text(Temperatures.From(Temperatures.ToCelsius(value, from), to));
    }
}

/// <summary>
/// The settings window's fields, as the text the user types and the boxes they tick. Kept apart from
/// <see cref="AppSettings"/> because a text box can hold anything: <see cref="ToSettings"/> turns it into real settings
/// or says, in words, what is wrong. No secrets here; the password and the bot token have their own boxes.
/// </summary>
public sealed class SettingsForm
{
    public List<ThresholdForm> Thresholds { get; private set; } = [];

    /// <summary>The unit the limits are typed in. The settings keep Celsius; <see cref="ToSettings"/> converts back.</summary>
    public TemperatureUnit TemperatureUnit { get; private set; }

    public string CooldownMinutes { get; set; } = "";

    public bool ToastEnabled { get; set; }
    public bool SoundEnabled { get; set; }

    public bool EmailEnabled { get; set; }
    public string EmailHost { get; set; } = "";
    public string EmailPort { get; set; } = "";
    public MailSecurity EmailSecurity { get; set; }
    public string EmailUsername { get; set; } = "";
    public string EmailFrom { get; set; } = "";
    public string EmailTo { get; set; } = "";

    public bool TelegramEnabled { get; set; }
    public string TelegramChatId { get; set; } = "";

    public bool WarningToast { get; set; }
    public bool WarningSound { get; set; }
    public bool WarningEmail { get; set; }
    public bool WarningTelegram { get; set; }
    public bool CriticalToast { get; set; }
    public bool CriticalSound { get; set; }
    public bool CriticalEmail { get; set; }
    public bool CriticalTelegram { get; set; }

    public bool AutoProtectEnabled { get; set; }
    public string AutoProtectSeconds { get; set; } = "";

    /// <summary>One program name per line.</summary>
    public string ProcessNames { get; set; } = "";

    public bool Autostart { get; set; }

    public static SettingsForm From(AppSettings settings) => new()
    {
        TemperatureUnit = settings.TemperatureUnit,
        Thresholds = settings.Thresholds
            .Where(pair => !SensorKindInfo.IsDisplayOnly(pair.Key))
            .OrderBy(pair => (int)pair.Key)
            .Select(pair => new ThresholdForm
            {
                Kind = pair.Key,
                Label = SensorKindInfo.Label(pair.Key),
                Unit = SensorKindInfo.Unit(pair.Key, settings.TemperatureUnit),
                Enabled = pair.Value.Enabled,
                Warning = Text(SensorKindInfo.Shown(pair.Key, pair.Value.Warning, settings.TemperatureUnit)),
                Critical = Text(SensorKindInfo.Shown(pair.Key, pair.Value.Critical, settings.TemperatureUnit)),
            }).ToList(),
        CooldownMinutes = settings.CooldownMinutes.ToString(CultureInfo.InvariantCulture),

        ToastEnabled = settings.Toast.Enabled,
        SoundEnabled = settings.Sound.Enabled,

        EmailEnabled = settings.Email.Enabled,
        EmailHost = settings.Email.Host,
        EmailPort = settings.Email.Port.ToString(CultureInfo.InvariantCulture),
        EmailSecurity = settings.Email.Security,
        EmailUsername = settings.Email.Username,
        EmailFrom = settings.Email.From,
        EmailTo = settings.Email.To,

        TelegramEnabled = settings.Telegram.Enabled,
        TelegramChatId = settings.Telegram.ChatId,

        WarningToast = settings.Routing.Warning.Contains(ChannelKind.Toast),
        WarningSound = settings.Routing.Warning.Contains(ChannelKind.Sound),
        WarningEmail = settings.Routing.Warning.Contains(ChannelKind.Email),
        WarningTelegram = settings.Routing.Warning.Contains(ChannelKind.Telegram),
        CriticalToast = settings.Routing.Critical.Contains(ChannelKind.Toast),
        CriticalSound = settings.Routing.Critical.Contains(ChannelKind.Sound),
        CriticalEmail = settings.Routing.Critical.Contains(ChannelKind.Email),
        CriticalTelegram = settings.Routing.Critical.Contains(ChannelKind.Telegram),

        AutoProtectEnabled = settings.AutoProtect.Enabled,
        AutoProtectSeconds = Text(settings.AutoProtect.AfterSeconds),
        ProcessNames = string.Join(Environment.NewLine, settings.AutoProtect.ProcessNames),

        Autostart = settings.Autostart,
    };

    /// <summary>
    /// A copy of the form with the temperature limits shown in another unit (90 °C becomes 194 °F), so switching the unit does not
    /// change what the limits mean. The other fields are copied as they are.
    /// </summary>
    public SettingsForm WithUnit(TemperatureUnit unit)
    {
        if (unit == TemperatureUnit)
            return this;

        var copy = (SettingsForm)MemberwiseClone();
        copy.TemperatureUnit = unit;
        copy.Thresholds = Thresholds.Select(row => row.Converted(TemperatureUnit, unit)).ToList();
        return copy;
    }

    /// <summary>True when any field differs from <paramref name="other"/>. The settings page uses it to show "unsaved changes".</summary>
    public bool Differs(SettingsForm other) => Fingerprint() != other.Fingerprint();

    /// <summary>Every field in one string, so two forms can be compared. The separator cannot be typed in a text box.</summary>
    private string Fingerprint()
    {
        var fields = new List<object>
        {
            CooldownMinutes, ToastEnabled, SoundEnabled,
            EmailEnabled, EmailHost, EmailPort, EmailSecurity, EmailUsername, EmailFrom, EmailTo,
            TelegramEnabled, TelegramChatId,
            WarningToast, WarningSound, WarningEmail, WarningTelegram,
            CriticalToast, CriticalSound, CriticalEmail, CriticalTelegram,
            AutoProtectEnabled, AutoProtectSeconds, ProcessNames, Autostart, TemperatureUnit,
        };
        fields.AddRange(Thresholds.SelectMany(row => new object[] { row.Kind, row.Enabled, row.Warning, row.Critical }));
        return string.Join('\u001F', fields);
    }

    /// <summary>
    /// The settings these fields describe. Whatever the form does not show (advanced options, dismissed notices,
    /// the version) is kept from <paramref name="current"/>.
    /// </summary>
    /// <exception cref="ArgumentException">A field is not a number, or the combination is not valid. The message says which and why.</exception>
    public AppSettings ToSettings(AppSettings current)
    {
        var thresholds = Thresholds.ToDictionary(
            row => row.Kind,
            row => new ThresholdSetting(
                row.Enabled,
                Celsius(row.Kind, Number($"{row.Label}, warning", row.Warning)),
                Celsius(row.Kind, Number($"{row.Label}, critical", row.Critical))));

        var settings = current with
        {
            Thresholds = thresholds,
            Routing = new RoutingSettings
            {
                Warning = Channels(WarningToast, WarningSound, WarningEmail, WarningTelegram),
                Critical = Channels(CriticalToast, CriticalSound, CriticalEmail, CriticalTelegram),
            },
            Toast = new ChannelToggle { Enabled = ToastEnabled },
            Sound = new ChannelToggle { Enabled = SoundEnabled },
            Email = new EmailSettings
            {
                Enabled = EmailEnabled,
                Host = EmailHost.Trim(),
                Port = WholeNumber("Email port", EmailPort),
                Security = EmailSecurity,
                Username = EmailUsername.Trim(),
                From = EmailFrom.Trim(),
                To = EmailTo.Trim(),
            },
            Telegram = new TelegramSettings { Enabled = TelegramEnabled, ChatId = TelegramChatId.Trim() },
            CooldownMinutes = WholeNumber("Cooldown", CooldownMinutes),
            AutoProtect = new AutoProtectSettings
            {
                Enabled = AutoProtectEnabled,
                AfterSeconds = Number("Auto-protect seconds", AutoProtectSeconds),
                ProcessNames = Lines(ProcessNames),
            },
            Autostart = Autostart,
            TemperatureUnit = TemperatureUnit,
        };

        settings.Validate();
        return settings;
    }

    internal static string Text(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>A limit typed in the chosen unit, as Celsius for the settings. Rounded to three places so 183 °F is stored as 83.889, not 83.88888….</summary>
    private double Celsius(SensorKind kind, double typed) =>
        SensorKindInfo.IsTemperature(kind) ? Math.Round(Temperatures.ToCelsius(typed, TemperatureUnit), 3) : typed;

    /// <summary>A number typed with a point or a comma (90.5 or 90,5).</summary>
    private static double Number(string field, string text)
    {
        if (double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value))
            return value;

        throw new ArgumentException($"{field}: \"{text.Trim()}\" is not a number.");
    }

    private static int WholeNumber(string field, string text)
    {
        if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;

        throw new ArgumentException($"{field}: \"{text.Trim()}\" is not a whole number.");
    }

    private static List<ChannelKind> Channels(bool toast, bool sound, bool email, bool telegram)
    {
        var channels = new List<ChannelKind>();
        if (toast) channels.Add(ChannelKind.Toast);
        if (sound) channels.Add(ChannelKind.Sound);
        if (email) channels.Add(ChannelKind.Email);
        if (telegram) channels.Add(ChannelKind.Telegram);
        return channels;
    }

    private static List<string> Lines(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

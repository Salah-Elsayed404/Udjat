using Udjat.Core.Alerts;
using Udjat.Core.Sensors;
using Udjat.Core.Settings;
using Xunit;

namespace Udjat.Tests;

/// <summary>Tests for <see cref="AppSettings"/>: the defaults, what it hands to the monitor, and validation.</summary>
public class SettingsTests
{
    [Fact]
    public void Defaults_AreValid()
    {
        new AppSettings().Validate();
    }

    [Fact]
    public void Defaults_GiveSixEnabledThresholds_WithoutVramOrFans()
    {
        var thresholds = new AppSettings().ToThresholds();

        Assert.Equal(6, thresholds.Count);
        Assert.False(thresholds.ContainsKey(SensorKind.GpuVram));
        Assert.False(thresholds.ContainsKey(SensorKind.Fan));
        Assert.Equal(90, thresholds[SensorKind.CpuTemp].Warning);
        Assert.Equal(97, thresholds[SensorKind.CpuTemp].Critical);
        Assert.Equal(108, thresholds[SensorKind.GpuHotspot].Critical);
    }

    [Fact]
    public void ToThresholds_IncludesVram_WhenItIsTurnedOn()
    {
        var settings = new AppSettings();
        settings.Thresholds[SensorKind.GpuVram] = new(true, 95, 99);

        Assert.True(settings.ToThresholds().ContainsKey(SensorKind.GpuVram));
    }

    [Theory]
    [InlineData(SensorKind.Fan)]
    [InlineData(SensorKind.GpuFan)]
    [InlineData(SensorKind.CpuLoad)]
    [InlineData(SensorKind.GpuLoad)]
    public void ToThresholds_NeverIncludesDisplayOnlyKinds_EvenIfTheFileHasAnEntry(SensorKind kind)
    {
        var settings = new AppSettings();
        settings.Thresholds[kind] = new(true, 100, 200);

        Assert.False(settings.ToThresholds().ContainsKey(kind));
    }

    [Fact]
    public void TheTheme_IsDarkByDefault_AndAnUnknownOneIsRefused()
    {
        Assert.Equal(AppTheme.Dark, new AppSettings().Theme);
        Assert.Throws<ArgumentException>(new AppSettings { Theme = (AppTheme)9 }.Validate);
    }

    [Fact]
    public void Defaults_GiveTheMonitorOptionsFromTheArchitectureDoc()
    {
        var options = new AppSettings().ToMonitorOptions();

        Assert.Equal(TimeSpan.FromSeconds(2), options.PollInterval);
        Assert.Equal(3, options.Hysteresis);
        Assert.Equal(2, options.ConfirmReadings);
        Assert.Equal(TimeSpan.FromMinutes(10), options.Cooldown);
        Assert.Equal(TimeSpan.FromSeconds(60), options.SustainedCriticalAfter);
        options.Validate();
    }

    [Fact]
    public void MonitorOptions_FollowTheSettings()
    {
        var settings = new AppSettings
        {
            CooldownMinutes = 5,
            AutoProtect = new AutoProtectSettings { AfterSeconds = 30 },
            Advanced = new AdvancedSettings { PollSeconds = 1.5, Hysteresis = 4, ConfirmReadings = 3 },
        };

        var options = settings.ToMonitorOptions();

        Assert.Equal(TimeSpan.FromSeconds(1.5), options.PollInterval);
        Assert.Equal(4, options.Hysteresis);
        Assert.Equal(3, options.ConfirmReadings);
        Assert.Equal(TimeSpan.FromMinutes(5), options.Cooldown);
        Assert.Equal(TimeSpan.FromSeconds(30), options.SustainedCriticalAfter);
    }

    [Fact]
    public void Defaults_Routing_ToastForWarning_EverythingForCritical()
    {
        var routing = new AppSettings().Routing;

        Assert.Equal([ChannelKind.Toast], routing.Warning);
        Assert.Equal([ChannelKind.Toast, ChannelKind.Sound, ChannelKind.Email, ChannelKind.Telegram], routing.Critical);
    }

    [Fact]
    public void Defaults_Autostart_OnAndAutoProtectOff()
    {
        var settings = new AppSettings();

        Assert.True(settings.Autostart);
        Assert.False(settings.AutoProtect.Enabled);
        Assert.Empty(settings.AutoProtect.ProcessNames);
        Assert.False(settings.Email.Enabled);
        Assert.False(settings.Telegram.Enabled);
    }

    // --- Validation ---------------------------------------------------------

    [Fact]
    public void EnabledEmailAndTelegram_WithGoodValues_AreValid()
    {
        var settings = new AppSettings
        {
            Email = new EmailSettings { Enabled = true, Host = "smtp.example.com", From = "a@example.com", To = "b@example.com" },
            Telegram = new TelegramSettings { Enabled = true, ChatId = "-1001234567890" },
            AutoProtect = new AutoProtectSettings { ProcessNames = ["ollama", "LM Studio.exe"] },
        };

        settings.Validate();
    }

    [Fact]
    public void DisabledEmail_IsNotChecked()
    {
        var settings = new AppSettings { Email = new EmailSettings { Enabled = false, Host = "", Port = 0 } };

        settings.Validate();
    }

    [Theory]
    [InlineData("newer schema")]
    [InlineData("warning not below critical")]
    [InlineData("email host empty")]
    [InlineData("email port zero")]
    [InlineData("email port too big")]
    [InlineData("email from invalid")]
    [InlineData("email to invalid")]
    [InlineData("telegram chat id")]
    [InlineData("process name with a path")]
    [InlineData("process name empty")]
    [InlineData("negative cooldown")]
    [InlineData("poll interval zero")]
    [InlineData("time too large")]
    [InlineData("unknown channel")]
    public void BadSettings_AreRejected(string problem)
    {
        var goodEmail = new EmailSettings { Enabled = true, Host = "smtp.example.com", From = "a@example.com", To = "b@example.com" };
        var settings = problem switch
        {
            "newer schema" => new AppSettings { SchemaVersion = 99 },
            "warning not below critical" => WithThreshold(SensorKind.CpuTemp, new(true, 95, 90)),
            "email host empty" => new AppSettings { Email = goodEmail with { Host = " " } },
            "email port zero" => new AppSettings { Email = goodEmail with { Port = 0 } },
            "email port too big" => new AppSettings { Email = goodEmail with { Port = 70000 } },
            "email from invalid" => new AppSettings { Email = goodEmail with { From = "not an address" } },
            "email to invalid" => new AppSettings { Email = goodEmail with { To = "" } },
            "telegram chat id" => new AppSettings { Telegram = new TelegramSettings { Enabled = true, ChatId = "my chat" } },
            "process name with a path" => new AppSettings { AutoProtect = new AutoProtectSettings { ProcessNames = [@"C:\Tools\ollama.exe"] } },
            "process name empty" => new AppSettings { AutoProtect = new AutoProtectSettings { ProcessNames = [" .exe"] } },
            "negative cooldown" => new AppSettings { CooldownMinutes = -1 },
            "poll interval zero" => new AppSettings { Advanced = new AdvancedSettings { PollSeconds = 0 } },
            "time too large" => new AppSettings { CooldownMinutes = int.MaxValue, AutoProtect = new AutoProtectSettings { AfterSeconds = 1e300 } },
            "unknown channel" => new AppSettings { Routing = new RoutingSettings { Warning = [(ChannelKind)99] } },
            _ => throw new ArgumentOutOfRangeException(nameof(problem)),
        };

        Assert.Throws<ArgumentException>(settings.Validate);
    }

    [Fact]
    public void BadThreshold_NamesTheSensorInTheMessage()
    {
        var settings = WithThreshold(SensorKind.GpuHotspot, new(true, 120, 100));

        var error = Assert.Throws<ArgumentException>(settings.Validate);

        Assert.Contains("GPU hotspot", error.Message);
    }

    private static AppSettings WithThreshold(SensorKind kind, ThresholdSetting setting)
    {
        var settings = new AppSettings();
        settings.Thresholds[kind] = setting;
        return settings;
    }
}

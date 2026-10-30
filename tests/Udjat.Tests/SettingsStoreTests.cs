using Udjat.Core.Alerts;
using Udjat.Core.Sensors;
using Udjat.Core.Settings;
using Xunit;

namespace Udjat.Tests;

/// <summary>Tests for <see cref="SettingsStore"/>, each in its own temporary folder.</summary>
public class SettingsStoreTests
{
    [Fact]
    public void Load_WithNoFile_UsesDefaults_AndWritesThem()
    {
        using var folder = new TempFolder();
        var path = folder.File("settings.json");

        var result = new SettingsStore(path).Load();

        Assert.Null(result.Notice);
        Assert.True(result.IsNew);
        Assert.Equal(10, result.Settings.CooldownMinutes);
        Assert.True(File.Exists(path));
        Assert.False(new SettingsStore(path).Load().IsNew);  // the second start is no longer the first
    }

    [Fact]
    public void TheTheme_IsSavedAsAWord_AndLoadedBack()
    {
        using var folder = new TempFolder();
        var store = new SettingsStore(folder.File("settings.json"));

        store.Save(new AppSettings { Theme = AppTheme.Light });

        Assert.Contains("\"theme\": \"Light\"", File.ReadAllText(folder.File("settings.json")));
        Assert.Equal(AppTheme.Light, store.Load().Settings.Theme);
    }

    [Fact]
    public void DismissedNotices_AreSaved_AndLoadedBack()
    {
        using var folder = new TempFolder();
        var store = new SettingsStore(folder.File("settings.json"));

        store.Save(new AppSettings { DismissedNotices = ["cpu-driver", "first-run"] });

        Assert.Equal(["cpu-driver", "first-run"], store.Load().Settings.DismissedNotices);
        Assert.Empty(new AppSettings().DismissedNotices);
    }

    [Fact]
    public void Save_ThenLoad_GivesTheSameSettings()
    {
        using var folder = new TempFolder();
        var path = folder.File("settings.json");
        var store = new SettingsStore(path);
        var settings = new AppSettings
        {
            CooldownMinutes = 3,
            Autostart = false,
            Routing = new RoutingSettings { Warning = [ChannelKind.Telegram], Critical = [ChannelKind.Sound] },
            Email = new EmailSettings { Enabled = true, Host = "smtp.example.com", Security = MailSecurity.SslOnConnect, From = "a@example.com", To = "b@example.com" },
            AutoProtect = new AutoProtectSettings { Enabled = true, ProcessNames = ["ollama"] },
        };
        settings.Thresholds[SensorKind.CpuTemp] = new(false, 70, 80);

        store.Save(settings);
        var loaded = store.Load();

        Assert.Null(loaded.Notice);
        Assert.Equal(3, loaded.Settings.CooldownMinutes);
        Assert.False(loaded.Settings.Autostart);
        Assert.Equal([ChannelKind.Telegram], loaded.Settings.Routing.Warning);
        Assert.Equal([ChannelKind.Sound], loaded.Settings.Routing.Critical);
        Assert.Equal(MailSecurity.SslOnConnect, loaded.Settings.Email.Security);
        Assert.Equal(["ollama"], loaded.Settings.AutoProtect.ProcessNames);
        Assert.Equal(new ThresholdSetting(false, 70, 80), loaded.Settings.Thresholds[SensorKind.CpuTemp]);

        // Saving what was loaded changes nothing.
        var before = File.ReadAllText(path);
        store.Save(loaded.Settings);
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public void Save_WritesEnumsAsNames_AndLeavesNoTemporaryFile()
    {
        using var folder = new TempFolder();
        var path = folder.File("settings.json");

        new SettingsStore(path).Save(new AppSettings());

        var text = File.ReadAllText(path);
        Assert.Contains("\"StartTls\"", text);
        Assert.Contains("\"CpuTemp\"", text);
        Assert.Contains("\"cooldownMinutes\": 10", text);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Save_CreatesTheFolder_IfMissing()
    {
        using var folder = new TempFolder();
        var path = Path.Combine(folder.Root, "inner", "settings.json");

        new SettingsStore(path).Save(new AppSettings());

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Save_WithInvalidSettings_Throws_AndKeepsTheOldFile()
    {
        using var folder = new TempFolder();
        var path = folder.File("settings.json");
        var store = new SettingsStore(path);
        store.Save(new AppSettings());
        var before = File.ReadAllText(path);

        Assert.Throws<ArgumentException>(() => store.Save(new AppSettings { CooldownMinutes = -5 }));

        Assert.Equal(before, File.ReadAllText(path));
    }

    // --- Loading what a person wrote by hand ----------------------------------

    [Fact]
    public void Load_PartialFile_FillsTheRestFromDefaults()
    {
        using var folder = new TempFolder();
        var path = folder.File("settings.json");
        File.WriteAllText(path, """
            { "cooldownMinutes": 5, "thresholds": { "CpuTemp": { "warning": 80 } }, "telegram": { "chatId": "42" } }
            """);

        var settings = new SettingsStore(path).Load().Settings;

        Assert.Equal(5, settings.CooldownMinutes);
        Assert.Equal(new ThresholdSetting(true, 80, 97), settings.Thresholds[SensorKind.CpuTemp]);  // warning changed, critical kept
        Assert.Equal(new ThresholdSetting(true, 84, 90), settings.Thresholds[SensorKind.GpuTemp]);  // a whole kind kept
        Assert.Equal("42", settings.Telegram.ChatId);
        Assert.False(settings.Telegram.Enabled);
        Assert.Equal(587, settings.Email.Port);
    }

    [Fact]
    public void Load_IgnoresUnknownFields_AndAllowsCommentsAndTrailingCommas()
    {
        using var folder = new TempFolder();
        var path = folder.File("settings.json");
        File.WriteAllText(path, """
            {
              // a comment
              "somethingNew": { "x": 1 },
              "autostart": false,
            }
            """);

        var result = new SettingsStore(path).Load();

        Assert.Null(result.Notice);
        Assert.False(result.Settings.Autostart);
    }

    [Fact]
    public void Load_ArraysInTheFile_ReplaceTheDefaultOnes()
    {
        using var folder = new TempFolder();
        var path = folder.File("settings.json");
        File.WriteAllText(path, """{ "routing": { "critical": ["Telegram"] } }""");

        var routing = new SettingsStore(path).Load().Settings.Routing;

        Assert.Equal([ChannelKind.Telegram], routing.Critical);
        Assert.Equal([ChannelKind.Toast], routing.Warning);
    }

    // --- A file that cannot be used --------------------------------------------

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("[1, 2, 3]")]
    [InlineData("null")]
    [InlineData("""{ "cooldownMinutes": "ten" }""")]
    [InlineData("""{ "email": { "security": "Plain" } }""")]
    [InlineData("""{ "thresholds": { "CpuTemp": { "warning": 99, "critical": 50 } } }""")]
    [InlineData("""{ "schemaVersion": 99 }""")]
    public void Load_WithAnUnusableFile_KeepsItAside_AndUsesDefaults(string content)
    {
        using var folder = new TempFolder();
        var path = folder.File("settings.json");
        File.WriteAllText(path, content);

        var result = new SettingsStore(path).Load();

        Assert.NotNull(result.Notice);
        Assert.False(result.IsNew);  // there was a file; it just could not be used
        Assert.Contains("settings.invalid.json", result.Notice);
        Assert.Equal(content, File.ReadAllText(folder.File("settings.invalid.json")));
        Assert.Equal(10, result.Settings.CooldownMinutes);

        // The next start finds a good file and says nothing.
        Assert.Null(new SettingsStore(path).Load().Notice);
    }

    [Fact]
    public void Load_WithASecondUnusableFile_ReplacesTheOlderInvalidCopy()
    {
        using var folder = new TempFolder();
        var path = folder.File("settings.json");
        File.WriteAllText(folder.File("settings.invalid.json"), "old");
        File.WriteAllText(path, "{ broken again");

        new SettingsStore(path).Load();

        Assert.Equal("{ broken again", File.ReadAllText(folder.File("settings.invalid.json")));
    }
}

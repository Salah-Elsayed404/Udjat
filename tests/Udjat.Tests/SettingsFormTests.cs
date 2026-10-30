using Udjat.Core.Alerts;
using Udjat.Core.Sensors;
using Udjat.Core.Settings;
using Xunit;

namespace Udjat.Tests;

/// <summary>Tests for <see cref="SettingsForm"/>: the settings window's text boxes and tick boxes.</summary>
public class SettingsFormTests
{
    private static SettingsForm Form() => SettingsForm.From(new AppSettings());

    private static ThresholdForm Row(SettingsForm form, SensorKind kind) => form.Thresholds.Single(t => t.Kind == kind);

    // --- From settings to the form and back -------------------------------------------

    [Fact]
    public void TheDefaults_GoThroughTheForm_Unchanged()
    {
        var original = new AppSettings();

        var result = SettingsForm.From(original).ToSettings(original);

        Assert.Equal(original.Thresholds.Where(t => t.Key != SensorKind.Fan).OrderBy(t => t.Key), result.Thresholds.OrderBy(t => t.Key));
        Assert.Equal(original.Routing.Warning, result.Routing.Warning);
        Assert.Equal(original.Routing.Critical, result.Routing.Critical);
        Assert.Equal(original.CooldownMinutes, result.CooldownMinutes);
        Assert.Equal(original.Email, result.Email);
        Assert.Equal(original.Telegram, result.Telegram);
        Assert.Equal(original.AutoProtect.AfterSeconds, result.AutoProtect.AfterSeconds);
        Assert.Equal(original.Autostart, result.Autostart);
    }

    [Fact]
    public void TheForm_ShowsOneRowPerKind_WithoutFans_WithWordsAndUnits()
    {
        var form = Form();

        Assert.Equal(7, form.Thresholds.Count);
        Assert.DoesNotContain(form.Thresholds, t => t.Kind == SensorKind.Fan);
        var hotspot = Row(form, SensorKind.GpuHotspot);
        Assert.Equal("GPU hotspot", hotspot.Label);
        Assert.Equal("°C", hotspot.Unit);
        Assert.Equal("100", hotspot.Warning);
        Assert.Equal("108", hotspot.Critical);
        Assert.False(Row(form, SensorKind.GpuVram).Enabled);
    }

    [Fact]
    public void WhatTheFormDoesNotShow_IsKeptFromTheCurrentSettings()
    {
        var current = new AppSettings
        {
            DismissedNotices = ["cpu-driver"],
            Advanced = new AdvancedSettings { PollSeconds = 5, Hysteresis = 4, ConfirmReadings = 3 },
        };

        var result = SettingsForm.From(current).ToSettings(current);

        Assert.Equal(["cpu-driver"], result.DismissedNotices);
        Assert.Equal(new AdvancedSettings { PollSeconds = 5, Hysteresis = 4, ConfirmReadings = 3 }, result.Advanced);
    }

    // --- Typing ---------------------------------------------------------------------------

    [Fact]
    public void Numbers_CanBeTypedWithAPointOrAComma_AndSpaces()
    {
        var form = Form();
        Row(form, SensorKind.CpuTemp).Warning = " 85,5 ";
        Row(form, SensorKind.CpuTemp).Critical = "95.25";

        var result = form.ToSettings(new AppSettings());

        Assert.Equal(new ThresholdSetting(true, 85.5, 95.25), result.Thresholds[SensorKind.CpuTemp]);
    }

    [Fact]
    public void TheChannelsAndRouting_FollowTheTickBoxes()
    {
        var form = Form();
        form.SoundEnabled = false;
        form.WarningToast = false;
        form.WarningTelegram = true;
        form.CriticalSound = false;

        var result = form.ToSettings(new AppSettings());

        Assert.False(result.Sound.Enabled);
        Assert.Equal([ChannelKind.Telegram], result.Routing.Warning);
        Assert.Equal([ChannelKind.Toast, ChannelKind.Email, ChannelKind.Telegram], result.Routing.Critical);
    }

    [Fact]
    public void Email_AndTelegram_AreTakenFromTheirBoxes_Trimmed()
    {
        var form = Form();
        form.EmailEnabled = true;
        form.EmailHost = "  smtp.example.com ";
        form.EmailPort = "465";
        form.EmailSecurity = MailSecurity.SslOnConnect;
        form.EmailUsername = " me ";
        form.EmailFrom = " udjat@example.com ";
        form.EmailTo = "me@example.com";
        form.TelegramEnabled = true;
        form.TelegramChatId = " -1001234567890 ";

        var result = form.ToSettings(new AppSettings());

        Assert.Equal(new EmailSettings
        {
            Enabled = true, Host = "smtp.example.com", Port = 465, Security = MailSecurity.SslOnConnect,
            Username = "me", From = "udjat@example.com", To = "me@example.com",
        }, result.Email);
        Assert.Equal(new TelegramSettings { Enabled = true, ChatId = "-1001234567890" }, result.Telegram);
    }

    [Fact]
    public void ProgramNames_AreOnePerLine_WithoutBlanksOrRepeats()
    {
        var form = Form();
        form.AutoProtectEnabled = true;
        form.ProcessNames = "ollama\r\n\r\n  LM Studio.exe \nOLLAMA\n";

        var result = form.ToSettings(new AppSettings());

        Assert.True(result.AutoProtect.Enabled);
        Assert.Equal(["ollama", "LM Studio.exe"], result.AutoProtect.ProcessNames);
    }

    // --- Mistakes ---------------------------------------------------------------------------

    [Fact]
    public void ANumberThatIsNotANumber_SaysWhichBoxAndWhat()
    {
        var form = Form();
        Row(form, SensorKind.GpuHotspot).Warning = "hot";

        var error = Assert.Throws<ArgumentException>(() => form.ToSettings(new AppSettings()));

        Assert.Equal("GPU hotspot, warning: \"hot\" is not a number.", error.Message);
    }

    [Theory]
    [InlineData("cooldown", "Cooldown: \"ten\" is not a whole number.")]
    [InlineData("port", "Email port: \"25.5\" is not a whole number.")]
    [InlineData("seconds", "Auto-protect seconds: \"\" is not a number.")]
    public void OtherFields_SayWhichBoxIsWrong(string field, string expected)
    {
        var form = Form();
        switch (field)
        {
            case "cooldown": form.CooldownMinutes = "ten"; break;
            case "port": form.EmailPort = "25.5"; break;
            case "seconds": form.AutoProtectSeconds = ""; break;
        }

        var error = Assert.Throws<ArgumentException>(() => form.ToSettings(new AppSettings()));

        Assert.Equal(expected, error.Message);
    }

    [Fact]
    public void LimitsInTheWrongOrder_AreRefused_NamingTheSensor()
    {
        var form = Form();
        Row(form, SensorKind.Ram).Warning = "99";
        Row(form, SensorKind.Ram).Critical = "90";

        var error = Assert.Throws<ArgumentException>(() => form.ToSettings(new AppSettings()));

        Assert.Contains("RAM", error.Message);
    }

    [Fact]
    public void EmailOn_WithoutAServer_IsRefused()
    {
        var form = Form();
        form.EmailEnabled = true;

        Assert.Throws<ArgumentException>(() => form.ToSettings(new AppSettings()));
    }

    // --- Unsaved changes ---------------------------------------------------------------

    [Fact]
    public void TwoFormsFromTheSameSettings_DoNotDiffer()
    {
        Assert.False(Form().Differs(Form()));
    }

    [Fact]
    public void ChangingALimit_Differs_AndChangingItBack_DoesNot()
    {
        var saved = Form();
        var edited = Form();

        Row(edited, SensorKind.CpuTemp).Warning = "85";
        Assert.True(edited.Differs(saved));

        Row(edited, SensorKind.CpuTemp).Warning = Row(saved, SensorKind.CpuTemp).Warning;
        Assert.False(edited.Differs(saved));
    }

    [Fact]
    public void ChangingATickBoxOrATextBox_Differs()
    {
        var saved = Form();

        Assert.True(Edit(f => f.CriticalTelegram = !f.CriticalTelegram).Differs(saved));
        Assert.True(Edit(f => f.EmailHost = "smtp.example.com").Differs(saved));
        Assert.True(Edit(f => f.ProcessNames += "ollama").Differs(saved));
        Assert.True(Edit(f => f.Autostart = !f.Autostart).Differs(saved));
        Assert.True(Edit(f => Row(f, SensorKind.Ram).Enabled = !Row(f, SensorKind.Ram).Enabled).Differs(saved));
    }

    private static SettingsForm Edit(Action<SettingsForm> change)
    {
        var form = Form();
        change(form);
        return form;
    }

    [Fact]
    public void TheCurrentSettings_AreNotChangedByAFailedSave()
    {
        var current = new AppSettings();
        var form = SettingsForm.From(current);
        form.CooldownMinutes = "-3";

        Assert.Throws<ArgumentException>(() => form.ToSettings(current));

        Assert.Equal(10, current.CooldownMinutes);
    }
}

using Udjat.Core.Alerts;
using Udjat.Core.Monitoring;
using Udjat.Core.Sensors;
using Udjat.Core.Settings;
using Xunit;

namespace Udjat.Tests;

/// <summary>Tests for the wording of messages (<see cref="AlertText"/>) and for which channels get them (<see cref="ChannelRouter"/>).</summary>
public class AlertMessageTests
{
    private static readonly DateTimeOffset Time = new(2026, 10, 29, 18, 3, 12, TimeSpan.Zero);

    private static SensorEvent Event(SensorKind kind, double value, AlertLevel level) =>
        new(new SensorReading("gpu/x", kind, "NVIDIA GeForce RTX 3060", value), level, Time);

    private static string Local(string format) => Time.ToLocalTime().ToString(format, System.Globalization.CultureInfo.InvariantCulture);

    // --- Wording ----------------------------------------------------------------

    [Fact]
    public void CriticalAlert_HasTheTitleAndBodyFromTheSpec()
    {
        var message = AlertText.Alert(Event(SensorKind.GpuHotspot, 107.6, AlertLevel.Critical), limit: 108);

        Assert.Equal("Udjat: GPU hotspot is CRITICAL (108 °C)", message.Title);
        Assert.Equal($"NVIDIA GeForce RTX 3060: GPU hotspot is 108 °C. Critical limit: 108 °C. {Local("HH:mm, d MMM yyyy")}.", message.Body);
        Assert.Equal(AlertMessageKind.Alert, message.Kind);
        Assert.Equal(AlertLevel.Critical, message.Level);
        Assert.Equal("CRITICAL", message.Label);
        Assert.Equal("GPU hotspot", message.Subject);
        Assert.Equal("NVIDIA GeForce RTX 3060", message.Source);
        Assert.Equal("108 °C", message.Value);
        Assert.Equal(Time, message.Time);
    }

    [Fact]
    public void WarningAlert_SaysWarning_AndNamesTheWarningLimit()
    {
        var message = AlertText.Alert(Event(SensorKind.Ram, 97.4, AlertLevel.Warning), limit: 97);

        Assert.Equal("Udjat: RAM warning (97 %)", message.Title);
        Assert.Contains("Warning limit: 97 %.", message.Body);
        Assert.Equal("WARNING", message.Label);
    }

    [Fact]
    public void Recovered_SaysBackToNormal()
    {
        var message = AlertText.Recovered(Event(SensorKind.GpuHotspot, 85.2, AlertLevel.Normal));

        Assert.Equal("Udjat: GPU hotspot is back to normal (85 °C)", message.Title);
        Assert.Contains("is back to normal at 85 °C", message.Body);
        Assert.Equal(AlertMessageKind.Recovered, message.Kind);
        Assert.Equal(AlertLevel.Normal, message.Level);
        Assert.Equal("RECOVERED", message.Label);
    }

    [Fact]
    public void MonitoringStopped_NamesWhenTheLastReadingWas()
    {
        var since = Time.AddMinutes(-2);

        var message = AlertText.MonitoringStopped(since, Time);

        Assert.Equal($"Udjat cannot read the sensors (since {since.ToLocalTime():HH:mm}).", message.Title);
        Assert.Equal(AlertLevel.Critical, message.Level);
        Assert.Equal("STOPPED", message.Label);
        Assert.Equal("", message.Subject);
    }

    [Fact]
    public void MonitoringResumed_AndTest_HaveFixedTitles()
    {
        Assert.Equal("Udjat is reading the sensors again.", AlertText.MonitoringResumed(Time).Title);
        Assert.Equal("RESUMED", AlertText.MonitoringResumed(Time).Label);
        Assert.Equal("Udjat: test message", AlertText.Test(Time).Title);
        Assert.Equal("TEST", AlertText.Test(Time).Label);
    }

    // --- Routing ----------------------------------------------------------------

    private static AppSettings Everything() => new()
    {
        Email = new EmailSettings { Enabled = true },
        Telegram = new TelegramSettings { Enabled = true },
    };

    private static AlertMessage Alert(AlertLevel level) =>
        AlertText.Alert(Event(SensorKind.CpuTemp, 95, level), limit: 90);

    [Fact]
    public void DefaultSettings_Warning_IsAToastOnly()
    {
        Assert.Equal([ChannelKind.Toast], ChannelRouter.For(new AppSettings(), Alert(AlertLevel.Warning)));
    }

    [Fact]
    public void DefaultSettings_Critical_UsesOnlyTheChannelsThatAreSetUp()
    {
        // Email and Telegram are off until the user sets them up.
        Assert.Equal([ChannelKind.Toast, ChannelKind.Sound], ChannelRouter.For(new AppSettings(), Alert(AlertLevel.Critical)));
    }

    [Fact]
    public void Critical_UsesEveryChannel_OnceTheyAreSetUp()
    {
        var channels = ChannelRouter.For(Everything(), Alert(AlertLevel.Critical));

        Assert.Equal([ChannelKind.Toast, ChannelKind.Sound, ChannelKind.Email, ChannelKind.Telegram], channels);
    }

    [Fact]
    public void ASwitchedOffChannel_IsNotUsed_EvenIfRouted()
    {
        var settings = new AppSettings { Toast = new ChannelToggle { Enabled = false } };

        Assert.Empty(ChannelRouter.For(settings, Alert(AlertLevel.Warning)));
    }

    [Fact]
    public void Routing_FollowsTheUsersChoice_InAFixedOrder_WithoutRepeats()
    {
        var settings = Everything() with
        {
            Routing = new RoutingSettings { Warning = [ChannelKind.Telegram, ChannelKind.Sound, ChannelKind.Telegram] },
        };

        Assert.Equal([ChannelKind.Sound, ChannelKind.Telegram], ChannelRouter.For(settings, Alert(AlertLevel.Warning)));
    }

    [Fact]
    public void Recovered_GoesToTheChannelsThatHeardTheAlert_ExceptSound()
    {
        var message = AlertText.Recovered(Event(SensorKind.CpuTemp, 60, AlertLevel.Normal));
        ChannelKind[] heard = [ChannelKind.Toast, ChannelKind.Sound, ChannelKind.Email];

        Assert.Equal([ChannelKind.Toast, ChannelKind.Email], ChannelRouter.For(Everything(), message, heard));
    }

    [Fact]
    public void Recovered_SkipsAChannelSwitchedOffSince()
    {
        var message = AlertText.Recovered(Event(SensorKind.CpuTemp, 60, AlertLevel.Normal));
        ChannelKind[] heard = [ChannelKind.Toast, ChannelKind.Email];

        Assert.Equal([ChannelKind.Toast], ChannelRouter.For(new AppSettings(), message, heard));
    }

    [Fact]
    public void Recovered_WithNoKnownAlert_GoesNowhere()
    {
        var message = AlertText.Recovered(Event(SensorKind.CpuTemp, 60, AlertLevel.Normal));

        Assert.Empty(ChannelRouter.For(Everything(), message));
        Assert.Empty(ChannelRouter.For(Everything(), message, []));
    }

    [Fact]
    public void StoppedResumedAndAutoProtect_UseTheCriticalChannels_ExceptSound()
    {
        var settings = Everything();
        var stopped = AlertText.MonitoringStopped(Time, Time);
        var resumed = AlertText.MonitoringResumed(Time);
        var protect = new AlertMessage(AlertMessageKind.AutoProtect, AlertLevel.Critical, "t", "b", Time);
        ChannelKind[] expected = [ChannelKind.Toast, ChannelKind.Email, ChannelKind.Telegram];

        Assert.Equal(expected, ChannelRouter.For(settings, stopped));
        Assert.Equal(expected, ChannelRouter.For(settings, resumed));
        Assert.Equal(expected, ChannelRouter.For(settings, protect));
    }

    [Fact]
    public void ATestMessage_IsNotRouted_BecauseItGoesToOneChannel()
    {
        Assert.Empty(ChannelRouter.For(Everything(), AlertText.Test(Time)));
    }
}

using System.Text.Json;
using Udjat.Core.Alerts;
using Udjat.Core.Monitoring;
using Udjat.Core.Sensors;
using Udjat.Core.Settings;
using Udjat.Core.Status;
using Xunit;

namespace Udjat.Tests;

/// <summary>Tests for the Celsius or Fahrenheit choice: the conversions, every place a temperature is shown, and the limits in the settings form.</summary>
public class TemperatureUnitTests
{
    private static readonly DateTimeOffset Time = new(2026, 10, 30, 18, 3, 12, TimeSpan.Zero);
    private static readonly AppSettings Fahrenheit = new() { TemperatureUnit = TemperatureUnit.Fahrenheit };

    private static ThresholdForm Row(SettingsForm form, SensorKind kind) => form.Thresholds.Single(t => t.Kind == kind);

    // --- The conversions ------------------------------------------------------------------

    [Theory]
    [InlineData(0, 32)]
    [InlineData(100, 212)]
    [InlineData(-40, -40)]
    [InlineData(90, 194)]
    [InlineData(37, 98.6)]
    public void Celsius_ToFahrenheit_AndBack(double celsius, double fahrenheit)
    {
        Assert.Equal(fahrenheit, Temperatures.From(celsius, TemperatureUnit.Fahrenheit), 6);
        Assert.Equal(celsius, Temperatures.ToCelsius(fahrenheit, TemperatureUnit.Fahrenheit), 6);
        Assert.Equal(celsius, Temperatures.From(celsius, TemperatureUnit.Celsius));  // Celsius is left alone
    }

    [Fact]
    public void TheSymbol_FollowsTheUnit()
    {
        Assert.Equal("°C", Temperatures.Symbol(TemperatureUnit.Celsius));
        Assert.Equal("°F", Temperatures.Symbol(TemperatureUnit.Fahrenheit));
    }

    // --- What changes with the unit, and what does not ----------------------------------------

    [Theory]
    [InlineData(SensorKind.CpuTemp)]
    [InlineData(SensorKind.GpuTemp)]
    [InlineData(SensorKind.GpuHotspot)]
    [InlineData(SensorKind.DriveTemp)]
    [InlineData(SensorKind.NvmeTemp)]
    public void Temperatures_AreShownInTheChosenUnit(SensorKind kind)
    {
        Assert.True(SensorKindInfo.IsTemperature(kind));
        Assert.Equal("108 °C", SensorKindInfo.Format(kind, 108));
        Assert.Equal("226 °F", SensorKindInfo.Format(kind, 108, TemperatureUnit.Fahrenheit));
        Assert.Equal("°F", SensorKindInfo.Unit(kind, TemperatureUnit.Fahrenheit));
    }

    [Theory]
    [InlineData(SensorKind.Ram, "%")]
    [InlineData(SensorKind.GpuVram, "%")]
    [InlineData(SensorKind.CpuLoad, "%")]
    [InlineData(SensorKind.GpuLoad, "%")]
    [InlineData(SensorKind.Fan, "RPM")]
    [InlineData(SensorKind.GpuFan, "RPM")]
    public void PercentagesAndFanSpeeds_NeverChange(SensorKind kind, string unit)
    {
        Assert.False(SensorKindInfo.IsTemperature(kind));
        Assert.Equal($"90 {unit}", SensorKindInfo.Format(kind, 90, TemperatureUnit.Fahrenheit));
    }

    [Fact]
    public void TheSettings_DefaultToCelsius_AndKeepTheChoiceInTheFile()
    {
        Assert.Equal(TemperatureUnit.Celsius, new AppSettings().TemperatureUnit);

        var json = JsonSerializer.Serialize(Fahrenheit, new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });
        Assert.Contains("\"TemperatureUnit\":\"Fahrenheit\"", json);
        Assert.Throws<ArgumentException>(() => new AppSettings { TemperatureUnit = (TemperatureUnit)7 }.Validate());
    }

    // --- Where the user reads temperatures --------------------------------------------------

    [Fact]
    public void TheDashboard_ShowsValuesAndLimits_InTheChosenUnit()
    {
        var cpu = new SensorStatus(new SensorReading("cpu-temp", SensorKind.CpuTemp, "Core", 61, "Intel"), AlertLevel.Normal);

        var celsius = Assert.Single(DashboardModel.Panels(new PollResult(Time, [cpu]), new AppSettings()));
        var fahrenheit = Assert.Single(DashboardModel.Panels(new PollResult(Time, [cpu]), Fahrenheit));

        Assert.Equal("61 °C", celsius.PrimaryText);
        Assert.Equal("Warning 90 · Critical 97 °C", celsius.LimitsText);
        Assert.Equal("142 °F", fahrenheit.PrimaryText);          // 61 * 1.8 + 32 = 141.8
        Assert.Equal("Warning 194 · Critical 207 °F", fahrenheit.LimitsText);  // 97 * 1.8 + 32 = 206.6
    }

    [Fact]
    public void AnAlert_SaysTheValueAndTheLimit_InTheChosenUnit()
    {
        var e = new SensorEvent(new SensorReading("gpu", SensorKind.GpuHotspot, "NVIDIA GeForce RTX 3060", 108), AlertLevel.Critical, Time);

        var message = AlertText.Alert(e, 108, TemperatureUnit.Fahrenheit);

        Assert.Equal("Udjat: GPU hotspot is CRITICAL (226 °F)", message.Title);
        Assert.Contains("Critical limit: 226 °F.", message.Body);
        Assert.Equal("226 °F", message.Value);  // the log says it the same way
        Assert.Equal("Udjat: GPU hotspot is back to normal (226 °F)", AlertText.Recovered(e, TemperatureUnit.Fahrenheit).Title);
    }

    [Fact]
    public void TheTrayTooltip_UsesTheChosenUnit()
    {
        var hot = new SensorStatus(new SensorReading("gpu", SensorKind.GpuHotspot, "x", 108), AlertLevel.Critical);

        var summary = StatusSummary.From(new PollResult(Time, [hot]), stopped: false, hasSource: true, TemperatureUnit.Fahrenheit);

        Assert.Equal("Udjat: GPU hotspot CRITICAL (226 °F)", summary.Text);
    }

    // --- The limits in the settings form --------------------------------------------------------

    [Fact]
    public void TheForm_ShowsLimitsInTheUnit_WithItsLabel()
    {
        var form = SettingsForm.From(Fahrenheit);

        var cpu = Row(form, SensorKind.CpuTemp);
        Assert.Equal(("194", "206.6", "°F"), (cpu.Warning, cpu.Critical, cpu.Unit));
        Assert.Equal("183.2", Row(form, SensorKind.GpuTemp).Warning);   // 84 °C
        Assert.Equal("97", Row(form, SensorKind.Ram).Warning);          // a percentage stays
        Assert.Equal("%", Row(form, SensorKind.Ram).Unit);
    }

    [Fact]
    public void SavingTheForm_KeepsTheLimitsInCelsius()
    {
        var form = SettingsForm.From(new AppSettings()).WithUnit(TemperatureUnit.Fahrenheit);
        Row(form, SensorKind.CpuTemp).Warning = "180";   // typed in Fahrenheit

        var saved = form.ToSettings(new AppSettings());

        Assert.Equal(TemperatureUnit.Fahrenheit, saved.TemperatureUnit);
        Assert.Equal(82.222, saved.Thresholds[SensorKind.CpuTemp].Warning);     // 180 °F, as Celsius
        Assert.Equal(97, saved.Thresholds[SensorKind.CpuTemp].Critical, 3);     // the one that was not touched did not drift
        Assert.Equal(97, saved.Thresholds[SensorKind.Ram].Warning);
    }

    [Fact]
    public void SwitchingTheUnit_ConvertsTheBoxes_AndSwitchingBackRestoresThem()
    {
        var original = SettingsForm.From(new AppSettings());

        var inFahrenheit = original.WithUnit(TemperatureUnit.Fahrenheit);
        Assert.Equal("194", Row(inFahrenheit, SensorKind.CpuTemp).Warning);
        Assert.Equal("°F", Row(inFahrenheit, SensorKind.CpuTemp).Unit);
        Assert.Equal("Celsius", original.TemperatureUnit.ToString());   // the original form is not changed

        var back = inFahrenheit.WithUnit(TemperatureUnit.Celsius);
        Assert.False(back.Differs(original));  // every box is as it was
    }

    [Fact]
    public void ABoxThatIsNotANumber_IsLeftAsTyped_WhenTheUnitChanges()
    {
        var form = SettingsForm.From(new AppSettings());
        Row(form, SensorKind.CpuTemp).Warning = "hot";
        Row(form, SensorKind.GpuTemp).Warning = "85,5";   // a comma works too

        var converted = form.WithUnit(TemperatureUnit.Fahrenheit);

        Assert.Equal("hot", Row(converted, SensorKind.CpuTemp).Warning);
        Assert.Equal("185.9", Row(converted, SensorKind.GpuTemp).Warning);
    }

    [Fact]
    public void ChangingTheUnit_IsAnUnsavedChange_OnTheSettingsPage()
    {
        var model = new SettingsModel();
        model.Load(new AppSettings(), false, false);
        var raised = new List<string?>();
        model.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        model.TemperatureUnit = TemperatureUnit.Fahrenheit;

        Assert.True(model.IsDirty);
        Assert.Contains("Form", raised);   // the page reads every box again
        Assert.Equal("194", Row(model.Form, SensorKind.CpuTemp).Warning);

        model.TemperatureUnit = TemperatureUnit.Celsius;
        Assert.False(model.IsDirty);
    }
}

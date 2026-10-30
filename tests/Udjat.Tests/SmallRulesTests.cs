using Udjat.Core.Alerts;
using Udjat.Core.Protection;
using Udjat.Core.Sensors;
using Xunit;

namespace Udjat.Tests;

/// <summary>Tests for the small text rules: process names and how a value is shown.</summary>
public class SmallRulesTests
{
    [Theory]
    [InlineData("ollama", "ollama")]
    [InlineData("  ollama  ", "ollama")]
    [InlineData("ollama.exe", "ollama")]
    [InlineData("OLLAMA.EXE", "OLLAMA")]
    [InlineData("LM Studio.exe", "LM Studio")]
    [InlineData("python3.11", "python3.11")]
    public void ProcessName_IsNormalized(string raw, string expected)
    {
        Assert.True(ProcessNames.TryNormalize(raw, out var name));
        Assert.Equal(expected, name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".exe")]
    [InlineData(@"C:\Tools\ollama.exe")]
    [InlineData("tools/ollama")]
    [InlineData("olla*")]
    [InlineData("ollama?")]
    [InlineData("a|b")]
    [InlineData("bad\u0007name")]
    public void ProcessName_IsRefused_IfItCouldBeAPathOrMatchManyProcesses(string? raw)
    {
        Assert.False(ProcessNames.TryNormalize(raw, out var name));
        Assert.Equal("", name);
    }

    [Theory]
    [InlineData(SensorKind.GpuHotspot, 107.6, "108 °C")]
    [InlineData(SensorKind.CpuTemp, 61.4, "61 °C")]
    [InlineData(SensorKind.Ram, 95.5, "96 %")]
    [InlineData(SensorKind.GpuVram, 39.76, "40 %")]
    [InlineData(SensorKind.Fan, 2062, "2062 RPM")]
    [InlineData(SensorKind.NvmeTemp, -0.2, "0 °C")]
    [InlineData(SensorKind.CpuLoad, 12.4, "12 %")]
    [InlineData(SensorKind.GpuLoad, 99.5, "100 %")]
    [InlineData(SensorKind.GpuFan, 2062, "2062 RPM")]
    public void Format_RoundsToAWholeNumber_WithTheUnit(SensorKind kind, double value, string expected)
    {
        Assert.Equal(expected, SensorKindInfo.Format(kind, value));
    }

    [Theory]
    [InlineData(SensorKind.Fan, true)]
    [InlineData(SensorKind.GpuFan, true)]
    [InlineData(SensorKind.CpuLoad, true)]
    [InlineData(SensorKind.GpuLoad, true)]
    [InlineData(SensorKind.CpuTemp, false)]
    [InlineData(SensorKind.GpuHotspot, false)]
    [InlineData(SensorKind.GpuVram, false)]
    [InlineData(SensorKind.Ram, false)]
    [InlineData(SensorKind.NvmeTemp, false)]
    public void OnlyFansAndLoad_AreDisplayOnly(SensorKind kind, bool expected)
    {
        Assert.Equal(expected, SensorKindInfo.IsDisplayOnly(kind));
    }

    [Fact]
    public void EveryKind_HasALabel_AndAUnit()
    {
        foreach (var kind in Enum.GetValues<SensorKind>())
        {
            Assert.False(string.IsNullOrWhiteSpace(SensorKindInfo.Label(kind)));
            Assert.False(string.IsNullOrWhiteSpace(SensorKindInfo.Unit(kind)));

            // None fell through to the raw enum name (a fan is simply called "Fan").
            if (kind != SensorKind.Fan)
                Assert.NotEqual(kind.ToString(), SensorKindInfo.Label(kind));
        }
    }
}

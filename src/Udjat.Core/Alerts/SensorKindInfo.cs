using System.Globalization;
using Udjat.Core.Sensors;

namespace Udjat.Core.Alerts;

/// <summary>The words and unit shown to the user for each kind of sensor.</summary>
public static class SensorKindInfo
{
    public static string Label(SensorKind kind) => kind switch
    {
        SensorKind.CpuTemp => "CPU temperature",
        SensorKind.GpuTemp => "GPU temperature",
        SensorKind.GpuHotspot => "GPU hotspot",
        SensorKind.GpuVram => "GPU memory",
        SensorKind.Fan => "Fan",
        SensorKind.Ram => "RAM",
        SensorKind.DriveTemp => "Drive temperature",
        SensorKind.NvmeTemp => "NVMe SSD temperature",
        SensorKind.CpuLoad => "CPU load",
        SensorKind.GpuLoad => "GPU load",
        SensorKind.GpuFan => "GPU fan",
        _ => kind.ToString(),
    };

    /// <summary>The unit of a kind: RPM, %, or the temperature unit the user chose.</summary>
    public static string Unit(SensorKind kind, TemperatureUnit temperature = TemperatureUnit.Celsius) => kind switch
    {
        SensorKind.Fan or SensorKind.GpuFan => "RPM",
        SensorKind.GpuVram or SensorKind.Ram or SensorKind.CpuLoad or SensorKind.GpuLoad => "%",
        _ => Temperatures.Symbol(temperature),
    };

    /// <summary>True for the kinds that are temperatures, the only ones the C or F choice changes.</summary>
    public static bool IsTemperature(SensorKind kind) =>
        kind is SensorKind.CpuTemp or SensorKind.GpuTemp or SensorKind.GpuHotspot or SensorKind.DriveTemp or SensorKind.NvmeTemp;

    /// <summary>The value as the user sees it: a temperature converted to their unit, anything else as it is. Not rounded.</summary>
    public static double Shown(SensorKind kind, double value, TemperatureUnit temperature) =>
        IsTemperature(kind) ? Temperatures.From(value, temperature) : value;

    /// <summary>
    /// True for readings that are shown but never alerted: fans (many stop at idle) and load (100 % is normal for
    /// local AI). They have no thresholds.
    /// </summary>
    public static bool IsDisplayOnly(SensorKind kind) =>
        kind is SensorKind.Fan or SensorKind.GpuFan or SensorKind.CpuLoad or SensorKind.GpuLoad;

    /// <summary>The value rounded to a whole number, with its unit, for example "108 °C" (or "226 °F"). The value is in Celsius.</summary>
    public static string Format(SensorKind kind, double value, TemperatureUnit temperature = TemperatureUnit.Celsius) =>
        $"{Number(Shown(kind, value, temperature))} {Unit(kind, temperature)}";

    /// <summary>The value rounded to a whole number, for example "108".</summary>
    public static string Number(double value)
    {
        var rounded = Math.Round(value, MidpointRounding.AwayFromZero) + 0.0;  // + 0.0 turns -0 into 0
        return rounded.ToString("0", CultureInfo.InvariantCulture);
    }

    /// <summary>The value rounded to a whole number with a thousands separator, for example "1,460". Used for fan speeds.</summary>
    public static string Thousands(double value)
    {
        var rounded = Math.Round(value, MidpointRounding.AwayFromZero) + 0.0;
        return rounded.ToString("N0", CultureInfo.InvariantCulture);
    }
}

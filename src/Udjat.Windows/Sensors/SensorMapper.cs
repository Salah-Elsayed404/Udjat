using LibreHardwareMonitor.Hardware;
using Udjat.Core.Sensors;

namespace Udjat.Windows.Sensors;

/// <summary>What the mapper made of a list of raw sensors.</summary>
/// <param name="Readings">The readings Udjat shows. A sensor with no value is simply absent.</param>
/// <param name="CpuTemp">Whether CPU temperature could be read, and if not, why.</param>
public sealed record SensorMap(IReadOnlyList<SensorReading> Readings, CpuTempStatus CpuTemp);

/// <summary>
/// Picks the few sensors Udjat cares about out of everything LibreHardwareMonitor offers.
/// Pure logic with no hardware access. The rules and the reasons for them are in docs/SENSORS.md.
/// </summary>
public static class SensorMapper
{
    // Tried in this order; the first one that has a value wins. Names are LHM's own English strings.
    private static readonly string[] CpuPreferred =
        ["CPU Package", "Core (Tctl/Tdie)", "Core (Tdie)", "Core (Tctl)", "Core Max"];

    public static SensorMap Map(IReadOnlyList<RawSensor> raws)
    {
        var readings = new List<SensorReading>();
        var ids = new HashSet<string>();

        foreach (var hardware in raws.GroupBy(r => r.HardwareId))
        {
            var sensors = hardware.ToList();
            foreach (var reading in ForHardware(sensors).Concat(Fans(sensors)))
            {
                if (ids.Add(reading.Id))  // LHM ids can clash, so keep only the first of any id
                    readings.Add(reading);
            }
        }

        return new SensorMap(readings, CpuStatus(raws, readings));
    }

    /// <summary>The non-fan readings of one hardware item (all its sensors share one hardware id).</summary>
    private static IEnumerable<SensorReading> ForHardware(List<RawSensor> sensors) =>
        sensors[0].HardwareType switch
        {
            HardwareType.Cpu => Cpu(sensors),
            HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => Gpu(sensors),
            HardwareType.Memory => Memory(sensors),
            HardwareType.Storage => Storage(sensors),
            _ => [],
        };

    private static IEnumerable<SensorReading> Cpu(List<RawSensor> sensors)
    {
        var cpu = sensors[0];

        var temperature = FirstValue(sensors, SensorType.Temperature, CpuPreferred) ?? HottestCpuTemp(sensors);
        if (temperature is { } temp)
            yield return Reading(cpu, "cpu-temp", SensorKind.CpuTemp, cpu.HardwareName, temp);

        if (Value(sensors, SensorType.Load, "CPU Total") is { } load)
            yield return Reading(cpu, "cpu-load", SensorKind.CpuLoad, cpu.HardwareName, load);
    }

    /// <summary>Fallback for CPUs with unfamiliar names. "Distance to TjMax" counts down to the limit, so it is not a temperature.</summary>
    private static double? HottestCpuTemp(List<RawSensor> sensors) =>
        sensors
            .Where(s => s.SensorType == SensorType.Temperature
                        && !s.SensorName.Contains("Distance to TjMax", StringComparison.OrdinalIgnoreCase)
                        && HasValue(s))
            .Max(s => s.Value);

    private static IEnumerable<SensorReading> Gpu(List<RawSensor> sensors)
    {
        var gpu = sensors[0];

        if (Value(sensors, SensorType.Temperature, "GPU Core") is { } core)
            yield return Reading(gpu, "gpu-temp", SensorKind.GpuTemp, gpu.HardwareName, core);

        if (Value(sensors, SensorType.Temperature, "GPU Hot Spot") is { } hotspot)
            yield return Reading(gpu, "gpu-hotspot", SensorKind.GpuHotspot, gpu.HardwareName, hotspot);

        // The "GPU Core" load, how busy the card is. Not "GPU Memory" or "GPU Bus", which are different things.
        if (Value(sensors, SensorType.Load, "GPU Core") is { } load)
            yield return Reading(gpu, "gpu-load", SensorKind.GpuLoad, gpu.HardwareName, load);

        // Not the Load "GPU Memory" sensor: its LHM id clashes with "GPU Bus".
        if (Value(sensors, SensorType.SmallData, "GPU Memory Used") is { } used
            && Value(sensors, SensorType.SmallData, "GPU Memory Total") is { } total
            && total > 0)
            yield return Reading(gpu, "vram", SensorKind.GpuVram, gpu.HardwareName, used / total * 100, totalGb: total / 1024);
    }

    /// <summary>Only the hardware "/ram". The one called "Virtual Memory" ("/vram") is Windows virtual memory, not video memory.</summary>
    private static IEnumerable<SensorReading> Memory(List<RawSensor> sensors)
    {
        if (sensors[0].HardwareId != "/ram" || Value(sensors, SensorType.Load, "Memory") is not { } used)
            yield break;

        // The size is what is used plus what is available, both in GB.
        double? total = Value(sensors, SensorType.Data, "Memory Used") is { } usedGb && Value(sensors, SensorType.Data, "Memory Available") is { } freeGb
            ? usedGb + freeGb
            : null;

        yield return Reading(sensors[0], "ram", SensorKind.Ram, "System memory", used, total);
    }

    /// <summary>
    /// One temperature per drive. A drive also lists its own "Warning Temperature" and "Critical Temperature":
    /// those are the drive's limits, not readings, and are never matched here.
    /// </summary>
    private static IEnumerable<SensorReading> Storage(List<RawSensor> sensors)
    {
        var drive = sensors[0];
        var isNvme = drive.HardwareId.StartsWith("/nvme/", StringComparison.OrdinalIgnoreCase);

        var value = isNvme
            ? FirstValue(sensors, SensorType.Temperature, ["Composite Temperature", "Temperature"])
            : FirstValue(sensors, SensorType.Temperature, ["Temperature", "Composite Temperature"]);

        if (value is { } temp)
            yield return Reading(drive, "drive-temp", isNvme ? SensorKind.NvmeTemp : SensorKind.DriveTemp, drive.HardwareName, temp);
    }

    /// <summary>
    /// Every fan on any hardware. Fans on a graphics card are <see cref="SensorKind.GpuFan"/>; the rest (the motherboard's
    /// SuperIO chip, coolers) are <see cref="SensorKind.Fan"/>. Fans keep LHM's own id and name; the device says where they are.
    /// </summary>
    private static IEnumerable<SensorReading> Fans(List<RawSensor> sensors)
    {
        var onGraphicsCard = sensors[0].HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;
        var kind = onGraphicsCard ? SensorKind.GpuFan : SensorKind.Fan;

        return sensors
            .Where(s => s.SensorType == SensorType.Fan && HasValue(s))
            .Select(s => new SensorReading(s.SensorId, kind, s.SensorName, s.Value!.Value, s.HardwareName));
    }

    /// <summary>"No CPU temperature sensors" and "sensors with no values" mean different things to the user.</summary>
    private static CpuTempStatus CpuStatus(IReadOnlyList<RawSensor> raws, List<SensorReading> readings)
    {
        if (readings.Any(r => r.Kind == SensorKind.CpuTemp))
            return CpuTempStatus.Available;

        var hasCpuTempSensors = raws.Any(r => r.HardwareType == HardwareType.Cpu && r.SensorType == SensorType.Temperature);
        return hasCpuTempSensors ? CpuTempStatus.NoValues : CpuTempStatus.NotFound;
    }

    private static SensorReading Reading(RawSensor hardware, string suffix, SensorKind kind, string name, double value, double? totalGb = null) =>
        new($"{hardware.HardwareId}/{suffix}", kind, name, value, hardware.HardwareName, totalGb);

    /// <summary>The value of the sensor with this type and name (case-insensitive), if it has a usable one.</summary>
    private static double? Value(List<RawSensor> sensors, SensorType type, string name) =>
        sensors
            .Where(s => s.SensorType == type && HasValue(s) && s.SensorName.Equals(name, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Value)
            .FirstOrDefault();

    /// <summary>The value of the first name in the list that has one.</summary>
    private static double? FirstValue(List<RawSensor> sensors, SensorType type, string[] names) =>
        names.Select(name => Value(sensors, type, name)).FirstOrDefault(v => v is not null);

    private static bool HasValue(RawSensor sensor) => sensor.Value is { } v && double.IsFinite(v);
}

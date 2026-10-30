using LibreHardwareMonitor.Hardware;
using Udjat.Windows.Sensors;

namespace Udjat.Tests;

/// <summary>A piece of hardware that builds <see cref="RawSensor"/>s, so a test does not repeat its id and name.</summary>
internal sealed class FakeHardware(HardwareType type, string id, string name)
{
    /// <summary>The sensor id defaults to "hardware id/type/name"; pass one to reproduce an LHM id exactly.</summary>
    public RawSensor Sensor(SensorType sensorType, string sensorName, double? value, string? sensorId = null) =>
        new(type, id, name, sensorType, sensorId ?? $"{id}/{sensorType.ToString().ToLowerInvariant()}/{sensorName}", sensorName, value);
}

/// <summary>
/// The owner's PC as tools/SensorProbe listed it (RESEARCH.md): Intel CPU, NVIDIA RTX 3060,
/// one NVMe SSD and one hard disk, with PawnIO not installed. Only sensors that matter for the mapping are included,
/// but with their real names, ids and values.
/// </summary>
internal static class OwnerPc
{
    public static List<RawSensor> Sensors(bool cpuHasValues)
    {
        var cpu = new FakeHardware(HardwareType.Cpu, "/intelcpu/0", "13th Gen Intel Core i5-13400F");
        var virtualMemory = new FakeHardware(HardwareType.Memory, "/vram", "Virtual Memory");
        var memory = new FakeHardware(HardwareType.Memory, "/ram", "Total Memory");
        var gpu = new FakeHardware(HardwareType.GpuNvidia, "/gpu-nvidia/0", "NVIDIA GeForce RTX 3060");
        var nvme = new FakeHardware(HardwareType.Storage, "/nvme/1", "HS-SSD-WAVE(P) 512G");
        var hdd = new FakeHardware(HardwareType.Storage, "/hdd/0", "WDC WD20EARZ-00C5XB0");

        // Without the PawnIO driver every CPU temperature exists but has no value.
        double? WhenReadable(double value) => cpuHasValues ? value : null;

        return
        [
            cpu.Sensor(SensorType.Temperature, "Core Max", WhenReadable(64)),
            cpu.Sensor(SensorType.Temperature, "Core Average", WhenReadable(58)),
            cpu.Sensor(SensorType.Temperature, "CPU Package", WhenReadable(61)),
            cpu.Sensor(SensorType.Temperature, "P-Core #1", WhenReadable(60)),
            cpu.Sensor(SensorType.Temperature, "P-Core #1 Distance to TjMax", WhenReadable(40)),
            cpu.Sensor(SensorType.Load, "CPU Total", 51.85),

            virtualMemory.Sensor(SensorType.Load, "Memory", 96.92),
            memory.Sensor(SensorType.Load, "Memory", 95.25),
            memory.Sensor(SensorType.Data, "Memory Used", 15.09),
            memory.Sensor(SensorType.Data, "Memory Available", 0.75),

            gpu.Sensor(SensorType.Temperature, "GPU Core", 73),
            gpu.Sensor(SensorType.Temperature, "GPU Hot Spot", 84.25),
            gpu.Sensor(SensorType.Load, "GPU Core", 10),
            gpu.Sensor(SensorType.Load, "GPU Bus", 2, "/gpu-nvidia/0/load/3"),
            gpu.Sensor(SensorType.Load, "GPU Memory", 39.77, "/gpu-nvidia/0/load/3"),  // same id as "GPU Bus"
            gpu.Sensor(SensorType.Fan, "GPU Fan 1", 2062, "/gpu-nvidia/0/fan/1"),
            gpu.Sensor(SensorType.Fan, "GPU Fan 2", 2063, "/gpu-nvidia/0/fan/2"),
            gpu.Sensor(SensorType.SmallData, "GPU Memory Free", 7401),
            gpu.Sensor(SensorType.SmallData, "GPU Memory Used", 4886),
            gpu.Sensor(SensorType.SmallData, "GPU Memory Total", 12288),

            nvme.Sensor(SensorType.Temperature, "Composite Temperature", 44),
            nvme.Sensor(SensorType.Temperature, "Temperature #1", 43.85),
            nvme.Sensor(SensorType.Temperature, "Temperature #2", 42.85),
            nvme.Sensor(SensorType.Temperature, "Warning Temperature", 89),
            nvme.Sensor(SensorType.Temperature, "Critical Temperature", 94),

            hdd.Sensor(SensorType.Temperature, "Temperature", 34),
        ];
    }
}

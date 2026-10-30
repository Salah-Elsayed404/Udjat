using LibreHardwareMonitor.Hardware;
using Udjat.Core.Sensors;
using Udjat.Windows.Sensors;
using Xunit;

namespace Udjat.Tests;

/// <summary>Tests for the mapping rules the dashboard panels rely on: load, device names, sizes and fans.</summary>
public class SensorMapperDeviceTests
{
    private static readonly FakeHardware Cpu = new(HardwareType.Cpu, "/amdcpu/0", "AMD Ryzen 7 5800X");
    private static readonly FakeHardware Gpu = new(HardwareType.GpuNvidia, "/gpu-nvidia/0", "NVIDIA GeForce RTX 3060");

    private static SensorMap Map(params RawSensor[] raws) => SensorMapper.Map(raws);

    private static Dictionary<string, SensorReading> ById(SensorMap map) => map.Readings.ToDictionary(r => r.Id);

    private static void AssertReading(SensorReading reading, SensorKind kind, double value)
    {
        Assert.Equal(kind, reading.Kind);
        Assert.Equal(value, reading.Value, 6);
    }

    // --- Load, devices and sizes (for the dashboard panels) ------------------------------------

    [Fact]
    public void Load_IsTakenFromTheTotalCpuAndTheGpuCore_NotFromTheOtherLoadSensors()
    {
        var map = Map(
            Cpu.Sensor(SensorType.Load, "CPU Core #1", 80),
            Cpu.Sensor(SensorType.Load, "CPU Total", 35),
            Gpu.Sensor(SensorType.Load, "GPU Memory", 40),
            Gpu.Sensor(SensorType.Load, "GPU Core", 96));

        var byId = ById(map);
        AssertReading(byId["/amdcpu/0/cpu-load"], SensorKind.CpuLoad, 35);
        AssertReading(byId["/gpu-nvidia/0/gpu-load"], SensorKind.GpuLoad, 96);
        Assert.Equal(2, map.Readings.Count);
    }

    [Fact]
    public void EveryReading_NamesItsDevice()
    {
        var byId = ById(SensorMapper.Map(OwnerPc.Sensors(cpuHasValues: true)));

        Assert.Equal("13th Gen Intel Core i5-13400F", byId["/intelcpu/0/cpu-temp"].Device);
        Assert.Equal("13th Gen Intel Core i5-13400F", byId["/intelcpu/0/cpu-load"].Device);
        Assert.Equal("NVIDIA GeForce RTX 3060", byId["/gpu-nvidia/0/gpu-hotspot"].Device);
        Assert.Equal("NVIDIA GeForce RTX 3060", byId["/gpu-nvidia/0/fan/1"].Device);
        Assert.Equal("HS-SSD-WAVE(P) 512G", byId["/nvme/1/drive-temp"].Device);
    }

    [Fact]
    public void MemoryAndVideoMemory_KnowTheirSize()
    {
        var byId = ById(SensorMapper.Map(OwnerPc.Sensors(cpuHasValues: false)));

        Assert.Equal(15.84, byId["/ram/ram"].TotalGb!.Value, 6);                  // used 15.09 + available 0.75
        Assert.Equal(12.0, byId["/gpu-nvidia/0/vram"].TotalGb!.Value, 6);         // 12288 MB
        Assert.Null(byId["/nvme/1/drive-temp"].TotalGb);
    }

    [Fact]
    public void Memory_WithoutBothFigures_HasNoSize()
    {
        var memory = new FakeHardware(HardwareType.Memory, "/ram", "Total Memory");

        var map = Map(
            memory.Sensor(SensorType.Load, "Memory", 50),
            memory.Sensor(SensorType.Data, "Memory Used", 8));

        Assert.Null(Assert.Single(map.Readings).TotalGb);
    }

    [Theory]
    [InlineData(HardwareType.GpuAmd)]
    [InlineData(HardwareType.GpuIntel)]
    public void FansOnAnyGraphicsCard_AreGpuFans(HardwareType type)
    {
        var gpu = new FakeHardware(type, "/gpu/0", "Some GPU");

        var map = Map(gpu.Sensor(SensorType.Fan, "GPU Fan", 1500, "/gpu/0/fan/0"));

        AssertReading(Assert.Single(map.Readings), SensorKind.GpuFan, 1500);
    }
}

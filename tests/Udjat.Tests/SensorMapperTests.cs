using LibreHardwareMonitor.Hardware;
using Udjat.Core.Sensors;
using Udjat.Windows.Sensors;
using Xunit;

namespace Udjat.Tests;

/// <summary>Tests for the rules that pick Udjat's sensors out of LibreHardwareMonitor's list. See docs/SENSORS.md.</summary>
public class SensorMapperTests
{
    private static readonly FakeHardware Cpu = new(HardwareType.Cpu, "/amdcpu/0", "AMD Ryzen 7 5800X");
    private static readonly FakeHardware Gpu = new(HardwareType.GpuNvidia, "/gpu-nvidia/0", "NVIDIA GeForce RTX 3060");
    private static readonly FakeHardware Nvme = new(HardwareType.Storage, "/nvme/0", "Some NVMe SSD");
    private static readonly FakeHardware Sata = new(HardwareType.Storage, "/ssd/0", "Some SATA SSD");

    private static SensorMap Map(params RawSensor[] raws) => SensorMapper.Map(raws);

    private static Dictionary<string, SensorReading> ById(SensorMap map) => map.Readings.ToDictionary(r => r.Id);

    private static void AssertReading(SensorReading reading, SensorKind kind, double value)
    {
        Assert.Equal(kind, reading.Kind);
        Assert.Equal(value, reading.Value, 6);
    }

    // --- The owner's PC --------------------------------------------------------

    [Fact]
    public void OwnerPc_WithoutPawnIo_ReadsEverythingExceptCpu()
    {
        var map = SensorMapper.Map(OwnerPc.Sensors(cpuHasValues: false));
        var byId = ById(map);

        Assert.Equal(10, map.Readings.Count);
        Assert.Equal(CpuTempStatus.NoValues, map.CpuTemp);
        AssertReading(byId["/intelcpu/0/cpu-load"], SensorKind.CpuLoad, 51.85);
        AssertReading(byId["/gpu-nvidia/0/gpu-load"], SensorKind.GpuLoad, 10);
        AssertReading(byId["/gpu-nvidia/0/gpu-temp"], SensorKind.GpuTemp, 73);
        AssertReading(byId["/gpu-nvidia/0/gpu-hotspot"], SensorKind.GpuHotspot, 84.25);
        AssertReading(byId["/gpu-nvidia/0/vram"], SensorKind.GpuVram, 4886.0 / 12288 * 100);
        AssertReading(byId["/gpu-nvidia/0/fan/1"], SensorKind.GpuFan, 2062);
        AssertReading(byId["/gpu-nvidia/0/fan/2"], SensorKind.GpuFan, 2063);
        AssertReading(byId["/nvme/1/drive-temp"], SensorKind.NvmeTemp, 44);
        AssertReading(byId["/hdd/0/drive-temp"], SensorKind.DriveTemp, 34);
        AssertReading(byId["/ram/ram"], SensorKind.Ram, 95.25);
    }

    [Fact]
    public void OwnerPc_WithPawnIo_AddsCpuTemperatureFromThePackage()
    {
        var map = SensorMapper.Map(OwnerPc.Sensors(cpuHasValues: true));

        Assert.Equal(11, map.Readings.Count);
        Assert.Equal(CpuTempStatus.Available, map.CpuTemp);
        AssertReading(ById(map)["/intelcpu/0/cpu-temp"], SensorKind.CpuTemp, 61);
    }

    [Fact]
    public void Readings_HaveReadableNames()
    {
        var byId = ById(SensorMapper.Map(OwnerPc.Sensors(cpuHasValues: true)));

        Assert.Equal("NVIDIA GeForce RTX 3060", byId["/gpu-nvidia/0/gpu-temp"].Name);
        Assert.Equal("GPU Fan 1", byId["/gpu-nvidia/0/fan/1"].Name);  // the device says where the fan is
        Assert.Equal("HS-SSD-WAVE(P) 512G", byId["/nvme/1/drive-temp"].Name);
        Assert.Equal("System memory", byId["/ram/ram"].Name);
        Assert.Equal("13th Gen Intel Core i5-13400F", byId["/intelcpu/0/cpu-temp"].Name);
    }

    // --- CPU --------------------------------------------------------------------

    [Fact]
    public void Cpu_AmdStyleNames_UseTctlTdie()
    {
        var map = Map(
            Cpu.Sensor(SensorType.Temperature, "CCD1 (Tdie)", 52),
            Cpu.Sensor(SensorType.Temperature, "Core (Tctl/Tdie)", 55));

        AssertReading(Assert.Single(map.Readings), SensorKind.CpuTemp, 55);
        Assert.Equal(CpuTempStatus.Available, map.CpuTemp);
    }

    [Fact]
    public void Cpu_PackagePreferredOverCoreMax()
    {
        var map = Map(
            Cpu.Sensor(SensorType.Temperature, "Core Max", 70),
            Cpu.Sensor(SensorType.Temperature, "CPU Package", 65));

        AssertReading(Assert.Single(map.Readings), SensorKind.CpuTemp, 65);
    }

    [Fact]
    public void Cpu_UnfamiliarNames_FallBackToTheHottest_IgnoringDistanceToTjMax()
    {
        var map = Map(
            Cpu.Sensor(SensorType.Temperature, "P-Core #1", 60),
            Cpu.Sensor(SensorType.Temperature, "E-Core #1", 70),
            Cpu.Sensor(SensorType.Temperature, "P-Core #1 Distance to TjMax", 95));

        AssertReading(Assert.Single(map.Readings), SensorKind.CpuTemp, 70);
    }

    [Fact]
    public void Cpu_PreferredNameWithoutValue_FallsThroughToTheNextOne()
    {
        var map = Map(
            Cpu.Sensor(SensorType.Temperature, "CPU Package", null),
            Cpu.Sensor(SensorType.Temperature, "Core Max", 66));

        AssertReading(Assert.Single(map.Readings), SensorKind.CpuTemp, 66);
    }

    [Fact]
    public void Cpu_WithOnlyLoadSensors_IsNotFound()
    {
        var map = Map(Cpu.Sensor(SensorType.Load, "CPU Total", 30));

        AssertReading(Assert.Single(map.Readings), SensorKind.CpuLoad, 30);  // the load is still shown
        Assert.Equal(CpuTempStatus.NotFound, map.CpuTemp);
    }

    [Fact]
    public void NoCpuAtAll_IsNotFound()
    {
        var map = Map(Gpu.Sensor(SensorType.Temperature, "GPU Core", 50));

        Assert.Equal(CpuTempStatus.NotFound, map.CpuTemp);
    }

    // --- GPU --------------------------------------------------------------------

    [Theory]
    [InlineData(HardwareType.GpuAmd)]
    [InlineData(HardwareType.GpuIntel)]
    public void OtherGpuVendors_AreMappedLikeNvidia(HardwareType type)
    {
        var gpu = new FakeHardware(type, "/gpu/0", "Some GPU");

        var map = Map(
            gpu.Sensor(SensorType.Temperature, "GPU Core", 61),
            gpu.Sensor(SensorType.Temperature, "GPU Hot Spot", 75));

        Assert.Equal(2, map.Readings.Count);
        AssertReading(ById(map)["/gpu/0/gpu-hotspot"], SensorKind.GpuHotspot, 75);
    }

    [Fact]
    public void Gpu_WithoutHotspot_GivesOnlyTheCoreTemperature()
    {
        var map = Map(Gpu.Sensor(SensorType.Temperature, "GPU Core", 50));

        AssertReading(Assert.Single(map.Readings), SensorKind.GpuTemp, 50);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Vram_WithoutARealTotal_GivesNoReading(double total)
    {
        var map = Map(
            Gpu.Sensor(SensorType.SmallData, "GPU Memory Used", 500),
            Gpu.Sensor(SensorType.SmallData, "GPU Memory Total", total));

        Assert.Empty(map.Readings);
    }

    [Fact]
    public void Vram_WithoutATotalSensor_GivesNoReading()
    {
        var map = Map(Gpu.Sensor(SensorType.SmallData, "GPU Memory Used", 500));

        Assert.Empty(map.Readings);
    }

    // --- Drives and memory --------------------------------------------------------

    [Fact]
    public void Nvme_DriveLimitsAndSecondarySensors_AreNeverReadings()
    {
        var map = Map(
            Nvme.Sensor(SensorType.Temperature, "Temperature #1", 43),
            Nvme.Sensor(SensorType.Temperature, "Warning Temperature", 89),
            Nvme.Sensor(SensorType.Temperature, "Critical Temperature", 94));

        Assert.Empty(map.Readings);
    }

    [Fact]
    public void Nvme_UsesTheCompositeTemperature_ElseTheTemperature()
    {
        var both = Map(
            Nvme.Sensor(SensorType.Temperature, "Temperature", 50),
            Nvme.Sensor(SensorType.Temperature, "Composite Temperature", 45));
        var plain = Map(Nvme.Sensor(SensorType.Temperature, "Temperature", 50));

        AssertReading(Assert.Single(both.Readings), SensorKind.NvmeTemp, 45);
        AssertReading(Assert.Single(plain.Readings), SensorKind.NvmeTemp, 50);
    }

    [Fact]
    public void OtherDrives_UseTheTemperature_ElseTheCompositeTemperature()
    {
        var both = Map(
            Sata.Sensor(SensorType.Temperature, "Composite Temperature", 45),
            Sata.Sensor(SensorType.Temperature, "Temperature", 40));
        var composite = Map(Sata.Sensor(SensorType.Temperature, "Composite Temperature", 45));

        AssertReading(Assert.Single(both.Readings), SensorKind.DriveTemp, 40);
        AssertReading(Assert.Single(composite.Readings), SensorKind.DriveTemp, 45);
    }

    [Fact]
    public void TwoNvmeDrives_GiveTwoReadings()
    {
        var other = new FakeHardware(HardwareType.Storage, "/nvme/1", "Other NVMe SSD");

        var map = Map(
            Nvme.Sensor(SensorType.Temperature, "Composite Temperature", 40),
            other.Sensor(SensorType.Temperature, "Composite Temperature", 55));

        Assert.Equal(["/nvme/0/drive-temp", "/nvme/1/drive-temp"], map.Readings.Select(r => r.Id));
    }

    [Fact]
    public void VirtualMemory_IsNotSystemMemory()
    {
        var virtualMemory = new FakeHardware(HardwareType.Memory, "/vram", "Virtual Memory");

        var map = Map(virtualMemory.Sensor(SensorType.Load, "Memory", 97));

        Assert.Empty(map.Readings);
    }

    // --- Fans, missing values, duplicates -------------------------------------------

    [Fact]
    public void Fans_FromSubHardware_AreReadAndAZeroRpmFanCounts()
    {
        var superIo = new FakeHardware(HardwareType.SuperIO, "/lpc/nct6798d", "Nuvoton NCT6798D");

        var map = Map(
            superIo.Sensor(SensorType.Fan, "Fan #1", 1200, "/lpc/nct6798d/fan/0"),
            superIo.Sensor(SensorType.Fan, "Fan #2", 0, "/lpc/nct6798d/fan/1"));

        Assert.Equal(2, map.Readings.Count);
        var stopped = ById(map)["/lpc/nct6798d/fan/1"];
        AssertReading(stopped, SensorKind.Fan, 0);  // not a graphics card, so a plain Fan
        Assert.Equal("Fan #2", stopped.Name);
        Assert.Equal("Nuvoton NCT6798D", stopped.Device);
    }

    [Fact]
    public void SensorsWithoutAUsableValue_AreSkipped()
    {
        var map = Map(
            Gpu.Sensor(SensorType.Temperature, "GPU Core", null),
            Gpu.Sensor(SensorType.Temperature, "GPU Hot Spot", double.NaN),
            Gpu.Sensor(SensorType.Fan, "GPU Fan 1", double.PositiveInfinity));

        Assert.Empty(map.Readings);
    }

    [Fact]
    public void ReadingsWithTheSameId_KeepOnlyTheFirst()
    {
        var map = Map(
            Gpu.Sensor(SensorType.Fan, "GPU Fan 1", 1000, "/clash/fan/0"),
            Gpu.Sensor(SensorType.Fan, "GPU Fan 2", 2000, "/clash/fan/0"));

        AssertReading(Assert.Single(map.Readings), SensorKind.GpuFan, 1000);
    }

    [Fact]
    public void OtherHardwareAndSensorTypes_AreIgnored()
    {
        var board = new FakeHardware(HardwareType.Motherboard, "/motherboard", "Some board");

        var map = Map(
            board.Sensor(SensorType.Temperature, "System", 35),
            Gpu.Sensor(SensorType.Load, "GPU Bus", 90),
            Gpu.Sensor(SensorType.Power, "GPU Package", 120));

        Assert.Empty(map.Readings);
    }

    [Fact]
    public void EmptyList_GivesNothing()
    {
        var map = SensorMapper.Map([]);

        Assert.Empty(map.Readings);
        Assert.Equal(CpuTempStatus.NotFound, map.CpuTemp);
    }
}

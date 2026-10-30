using LibreHardwareMonitor.Hardware;

namespace Udjat.Windows.Sensors;

/// <summary>
/// One LibreHardwareMonitor sensor, flattened into plain data so the mapping rules can be
/// tested with hand-built lists and no real hardware.
/// </summary>
/// <param name="HardwareType">Type of the hardware item the sensor belongs to (sub-hardware has its own).</param>
/// <param name="HardwareId">LHM identifier of that hardware item, for example "/gpu-nvidia/0".</param>
/// <param name="HardwareName">Name of that hardware item, for example "NVIDIA GeForce RTX 3060".</param>
/// <param name="SensorType">What the sensor measures.</param>
/// <param name="SensorId">LHM identifier of the sensor. Not always unique.</param>
/// <param name="SensorName">Name of the sensor, for example "GPU Hot Spot".</param>
/// <param name="Value">Current value, or null when LHM has none (for example a driver is missing).</param>
public sealed record RawSensor(
    HardwareType HardwareType,
    string HardwareId,
    string HardwareName,
    SensorType SensorType,
    string SensorId,
    string SensorName,
    double? Value);

namespace Udjat.Core.Sensors;

/// <summary>
/// One value read from one physical sensor.
/// The unit follows the kind: °C for temperatures, % used for VRAM and RAM, RPM for fans.
/// </summary>
/// <param name="Id">Stable and unique per physical sensor. Two drives have two different ids.</param>
/// <param name="Kind">What the sensor measures.</param>
/// <param name="Name">Human-readable name, shown in the UI and in alerts.</param>
/// <param name="Value">The current value.</param>
/// <param name="Device">The hardware it belongs to, for example "NVIDIA GeForce RTX 3060". The dashboard groups readings by it.</param>
/// <param name="TotalGb">For a reading that is a share of something (RAM, video memory), the size of that something in GB.</param>
public sealed record SensorReading(string Id, SensorKind Kind, string Name, double Value, string Device = "", double? TotalGb = null);

namespace Udjat.Core.Sensors;

/// <summary>The fixed, curated set of sensors Udjat watches.</summary>
public enum SensorKind
{
    CpuTemp,
    GpuTemp,
    GpuHotspot,
    GpuVram,
    Fan,
    Ram,

    /// <summary>Hard disks and SATA SSDs.</summary>
    DriveTemp,

    /// <summary>NVMe SSDs. Separate from <see cref="DriveTemp"/> because they run much hotter by design.</summary>
    NvmeTemp,

    /// <summary>How busy the processor is, in percent. Display only: high load is normal and never alerts.</summary>
    CpuLoad,

    /// <summary>How busy the graphics card is, in percent. Display only, like <see cref="CpuLoad"/>.</summary>
    GpuLoad,

    /// <summary>A fan on the graphics card. Display only. Other fans (the motherboard's) are <see cref="Fan"/>.</summary>
    GpuFan,
}

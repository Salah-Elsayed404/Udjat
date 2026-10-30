namespace Udjat.Core.Sensors;

/// <summary>
/// Whether CPU temperature could be read. LibreHardwareMonitor reports a missing driver silently
/// (the sensors exist but have no value), so this is the only way the app can tell.
/// </summary>
public enum CpuTempStatus
{
    /// <summary>A CPU temperature reading was produced.</summary>
    Available,

    /// <summary>CPU temperature sensors exist but all are empty: no administrator rights or no PawnIO driver.</summary>
    NoValues,

    /// <summary>There are no CPU temperature sensors at all: this CPU is not supported.</summary>
    NotFound,
}

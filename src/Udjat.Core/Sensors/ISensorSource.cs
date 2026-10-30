namespace Udjat.Core.Sensors;

/// <summary>
/// Where readings come from. The Windows app implements it with LibreHardwareMonitor;
/// a Linux port could implement it differently without touching the monitoring logic.
/// </summary>
public interface ISensorSource
{
    /// <summary>
    /// Reads every sensor it can get right now.
    /// A sensor that cannot be read (for example CPU temperature without the PawnIO driver)
    /// is simply left out of the result.
    /// </summary>
    IReadOnlyList<SensorReading> ReadAll();
}

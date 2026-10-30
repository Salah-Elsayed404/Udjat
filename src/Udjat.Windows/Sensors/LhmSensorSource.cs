using System.Diagnostics;
using System.Runtime.ExceptionServices;
using LibreHardwareMonitor.Hardware;
using Udjat.Core.Sensors;

namespace Udjat.Windows.Sensors;

/// <summary>
/// Reads the sensors through LibreHardwareMonitor. This class is deliberately thin: it opens LHM,
/// updates it, flattens its tree into <see cref="RawSensor"/>s and hands them to <see cref="SensorMapper"/>,
/// where all the rules live. Needs real hardware to run, so it is checked with tools/SensorProbe.
/// Use from one thread at a time (the monitor's poll loop). See docs/SENSORS.md.
/// </summary>
public sealed class LhmSensorSource : ISensorSource, IDisposable
{
    // Drive temperatures change slowly, and every update asks the disk for its SMART data.
    private static readonly TimeSpan StorageRefresh = TimeSpan.FromSeconds(30);

    private readonly Computer _computer;
    private long? _storageUpdatedAt;  // Stopwatch timestamp of the last storage update

    /// <summary>Opens LibreHardwareMonitor. Throws if it cannot start; the app then runs without sensors and says so.</summary>
    public LhmSensorSource()
    {
        // Only the groups Udjat needs. Everything else stays off: less code running with administrator rights.
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,  // fans, once the PawnIO driver is installed
            IsStorageEnabled = true,
        };
        _computer.Open();  // LHM looks for the PawnIO driver here, so installing it later needs a restart
    }

    /// <summary>Whether CPU temperature was readable in the last <see cref="ReadAll"/>.</summary>
    public CpuTempStatus CpuTemp { get; private set; } = CpuTempStatus.NotFound;

    /// <summary>Every sensor LHM listed in the last <see cref="ReadAll"/>, before mapping. For diagnostics.</summary>
    public IReadOnlyList<RawSensor> LastRaw { get; private set; } = [];

    public IReadOnlyList<SensorReading> ReadAll()
    {
        UpdateHardware();

        var raws = new List<RawSensor>();
        Flatten(_computer.Hardware.ToArray(), raws);  // a copy: LHM adds and removes hardware (USB drives)

        var map = SensorMapper.Map(raws);
        LastRaw = raws;
        CpuTemp = map.CpuTemp;
        return map.Readings;
    }

    public void Dispose() => _computer.Close();

    /// <summary>
    /// Updates every hardware item. One that throws is skipped for this poll; only if all of them
    /// fail does the first exception go up, and the monitor reports it as a failed poll.
    /// </summary>
    private void UpdateHardware()
    {
        var storageDue = _storageUpdatedAt is not { } at || Stopwatch.GetElapsedTime(at) >= StorageRefresh;
        if (storageDue)
            _storageUpdatedAt = Stopwatch.GetTimestamp();

        var attempts = 0;
        var failures = new List<Exception>();
        UpdateGroup(_computer.Hardware.ToArray(), storageDue, ref attempts, failures);

        if (attempts > 0 && failures.Count == attempts)
            ExceptionDispatchInfo.Throw(failures[0]);
    }

    private static void UpdateGroup(IEnumerable<IHardware> group, bool storageDue, ref int attempts, List<Exception> failures)
    {
        foreach (var hardware in group)
        {
            // Storage keeps its last values in between its slower updates.
            if (storageDue || hardware.HardwareType != HardwareType.Storage)
            {
                attempts++;
                try
                {
                    hardware.Update();
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }
            }

            UpdateGroup(hardware.SubHardware, storageDue, ref attempts, failures);
        }
    }

    private static void Flatten(IEnumerable<IHardware> group, List<RawSensor> into)
    {
        foreach (var hardware in group)
        {
            foreach (var sensor in hardware.Sensors)
            {
                into.Add(new RawSensor(
                    hardware.HardwareType,
                    hardware.Identifier.ToString(),
                    hardware.Name,
                    sensor.SensorType,
                    sensor.Identifier.ToString(),
                    sensor.Name,
                    sensor.Value));
            }

            Flatten(hardware.SubHardware, into);
        }
    }
}

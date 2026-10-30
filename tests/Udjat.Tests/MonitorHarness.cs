using Udjat.Core.Monitoring;
using Udjat.Core.Sensors;

namespace Udjat.Tests;

/// <summary>Short ways to build readings for the monitor tests.</summary>
internal static class Readings
{
    public static SensorReading Cpu(double value) => new("cpu", SensorKind.CpuTemp, "CPU Package", value);
    public static SensorReading Gpu(double value) => new("gpu", SensorKind.GpuTemp, "GPU Core", value);
    public static SensorReading Drive(string id, double value) => new(id, SensorKind.DriveTemp, id, value);
    public static SensorReading Fan(double rpm) => new("fan", SensorKind.Fan, "GPU Fan", rpm);
}

/// <summary>
/// A <see cref="SensorMonitor"/> wired to fakes, with every event recorded.
/// Thresholds: CPU and GPU 80/90, drives 50/60. Fans and RAM have none.
/// By default a single reading confirms a level, because the level rules are tested in ThresholdStateTests.
/// </summary>
internal sealed class Harness
{
    public static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    public FakeSensorSource Source { get; } = new();
    public FakeTimeProvider Clock { get; } = new(Start);
    public SensorMonitor Monitor { get; }

    public List<PollResult> Polls { get; } = [];
    public List<SensorEvent> Alerts { get; } = [];
    public List<SensorEvent> Recoveries { get; } = [];
    public List<SensorEvent> Sustained { get; } = [];
    public List<Exception> Failures { get; } = [];
    public List<string> Order { get; } = [];  // event names in the order they were raised

    public Harness(MonitorOptions? options = null)
    {
        var thresholds = new Dictionary<SensorKind, Threshold>
        {
            [SensorKind.CpuTemp] = new(80, 90),
            [SensorKind.GpuTemp] = new(80, 90),
            [SensorKind.DriveTemp] = new(50, 60),
        };
        options ??= new MonitorOptions { ConfirmReadings = 1, PollInterval = Interval };
        Monitor = new SensorMonitor(Source, thresholds, options, Clock);

        Monitor.Polled += (_, e) => { Polls.Add(e); Order.Add("Polled"); };
        Monitor.AlertRaised += (_, e) => { Alerts.Add(e); Order.Add("AlertRaised"); };
        Monitor.Recovered += (_, e) => { Recoveries.Add(e); Order.Add("Recovered"); };
        Monitor.CriticalSustained += (_, e) => { Sustained.Add(e); Order.Add("CriticalSustained"); };
        Monitor.PollFailed += (_, e) => Failures.Add(e);
    }

    /// <summary>Lets one poll interval pass, then polls once with these readings.</summary>
    public void Poll(params SensorReading[] readings)
    {
        Source.Readings = readings;
        Clock.Advance(Interval);
        Monitor.PollOnce();
    }
}

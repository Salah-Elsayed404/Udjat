using Udjat.Core.Monitoring;
using Udjat.Core.Sensors;
using Xunit;
using static Udjat.Tests.Harness;
using static Udjat.Tests.Readings;

namespace Udjat.Tests;

/// <summary>
/// Tests for the poll loop and its events. The level rules themselves are covered in
/// <see cref="ThresholdStateTests"/>, so here a single reading is enough to confirm a level.
/// </summary>
public class SensorMonitorTests
{
    // --- Polled -------------------------------------------------------------

    [Fact]
    public void Polled_ListsEveryFiniteReading_WithItsLevel()
    {
        var h = new Harness();

        h.Poll(Cpu(95), Gpu(50), Fan(1200), Drive("nvme", double.NaN));

        var poll = Assert.Single(h.Polls);
        var levels = poll.Sensors.ToDictionary(s => s.Reading.Id, s => s.Level);
        Assert.Equal(3, levels.Count);  // the NaN drive is left out
        Assert.Equal(AlertLevel.Critical, levels["cpu"]);
        Assert.Equal(AlertLevel.Normal, levels["gpu"]);
        Assert.Equal(AlertLevel.Normal, levels["fan"]);
        Assert.Equal(Start + Interval, poll.Time);
    }

    [Fact]
    public void Fans_NeverAlert()
    {
        var h = new Harness();

        h.Poll(Fan(0));
        h.Poll(Fan(99999));

        Assert.Empty(h.Alerts);
        Assert.All(h.Polls, p => Assert.Equal(AlertLevel.Normal, Assert.Single(p.Sensors).Level));
    }

    // --- Alerts, recovery, sustained ----------------------------------------

    [Fact]
    public void AlertRaised_CarriesReadingLevelAndTime()
    {
        var h = new Harness();

        h.Poll(Gpu(85));

        var alert = Assert.Single(h.Alerts);
        Assert.Equal("gpu", alert.Reading.Id);
        Assert.Equal(85, alert.Reading.Value);
        Assert.Equal(AlertLevel.Warning, alert.Level);
        Assert.Equal(Start + Interval, alert.Time);
    }

    [Fact]
    public void Recovered_IsRaised_WhenSensorReturnsToNormal()
    {
        var h = new Harness();
        h.Poll(Cpu(95));

        h.Poll(Cpu(50));

        var recovery = Assert.Single(h.Recoveries);
        Assert.Equal(AlertLevel.Normal, recovery.Level);
        Assert.Equal(50, recovery.Reading.Value);
    }

    [Fact]
    public void CriticalSustained_IsRaisedOnce_AfterTheConfiguredTime()
    {
        var h = new Harness(new MonitorOptions
        {
            ConfirmReadings = 1,
            PollInterval = Interval,
            SustainedCriticalAfter = TimeSpan.FromSeconds(6),
        });

        h.Poll(Cpu(95));  // t = 2 s: Critical begins
        h.Poll(Cpu(95));
        h.Poll(Cpu(95));
        Assert.Empty(h.Sustained);

        h.Poll(Cpu(95));  // t = 8 s: 6 s of Critical
        h.Poll(Cpu(95));

        var sustained = Assert.Single(h.Sustained);
        Assert.Equal("cpu", sustained.Reading.Id);
        Assert.Equal(Start + 4 * Interval, sustained.Time);
    }

    [Fact]
    public void Events_AreRaisedInOrder_PolledFirst()
    {
        var h = new Harness(new MonitorOptions
        {
            ConfirmReadings = 1,
            PollInterval = Interval,
            SustainedCriticalAfter = TimeSpan.Zero,
        });

        h.Poll(Cpu(95));

        Assert.Equal(["Polled", "AlertRaised", "CriticalSustained"], h.Order);
    }

    // --- Several sensors ----------------------------------------------------

    [Fact]
    public void TwoSensorsOfTheSameKind_AreIndependent()
    {
        var h = new Harness();

        h.Poll(Drive("drive-1", 55), Drive("drive-2", 40));
        h.Poll(Drive("drive-1", 55), Drive("drive-2", 55));

        // drive-1 alerted on the first poll and is in its cooldown; drive-2 alerts on its own account.
        Assert.Equal(["drive-1", "drive-2"], h.Alerts.Select(a => a.Reading.Id));
    }

    [Fact]
    public void ThresholdsOfAKindWithoutOne_AreSkipped()
    {
        var h = new Harness();

        h.Poll(new SensorReading("ram", SensorKind.Ram, "Memory", 99));

        Assert.Empty(h.Alerts);
        Assert.Single(h.Polls);
    }

    // --- Missing or unreadable sensors --------------------------------------

    [Fact]
    public void MissingOrNaNReading_KeepsTheSensorsState()
    {
        var h = new Harness();
        h.Poll(Cpu(95));  // Critical, alerted

        h.Poll();                  // CPU missing from the poll
        h.Poll(Cpu(double.NaN));   // CPU unreadable
        h.Poll(Cpu(95));           // still Critical: no new alert, no recovery
        Assert.Single(h.Alerts);
        Assert.Empty(h.Recoveries);

        h.Poll(Cpu(50));
        Assert.Single(h.Recoveries);
    }

    // --- Failures -----------------------------------------------------------

    [Fact]
    public void SourceThrows_RaisesPollFailed_AndTheNextPollWorks()
    {
        var h = new Harness();
        var error = new InvalidOperationException("sensor driver gone");
        h.Source.Failure = error;

        h.Monitor.PollOnce();  // must not throw

        Assert.Same(error, Assert.Single(h.Failures));
        Assert.Empty(h.Polls);

        h.Source.Failure = null;
        h.Poll(Cpu(50));
        Assert.Single(h.Polls);
    }

    [Fact]
    public void ThrowingHandler_DoesNotStopOtherHandlers_OrLaterEvents()
    {
        var h = new Harness();
        var error = new InvalidOperationException("handler bug");
        h.Monitor.Polled += (_, _) => throw error;
        var secondHandlerCalls = 0;
        h.Monitor.Polled += (_, _) => secondHandlerCalls++;

        h.Poll(Cpu(95));  // must not throw

        Assert.Equal(1, secondHandlerCalls);
        Assert.Single(h.Alerts);  // the alert still went out
        Assert.Same(error, Assert.Single(h.Failures));
    }

    [Fact]
    public void ThrowingPollFailedHandler_DoesNotPropagate()
    {
        var h = new Harness();
        h.Monitor.PollFailed += (_, _) => throw new InvalidOperationException("failure handler bug");
        h.Source.Failure = new InvalidOperationException("sensor driver gone");

        h.Monitor.PollOnce();  // must not throw

        Assert.Single(h.Failures);  // the well-behaved handler was still called
    }

    // --- Construction -------------------------------------------------------

    [Fact]
    public void InvalidThreshold_IsRejected_EvenForKindsNotYetSeen()
    {
        var thresholds = new Dictionary<SensorKind, Threshold> { [SensorKind.Ram] = new(95, 85) };

        Assert.Throws<ArgumentException>(() => new SensorMonitor(new FakeSensorSource(), thresholds, new MonitorOptions()));
    }

    [Fact]
    public void InvalidOptions_AreRejected()
    {
        var options = new MonitorOptions { ConfirmReadings = 0 };

        Assert.Throws<ArgumentException>(() => new SensorMonitor(new FakeSensorSource(), new Dictionary<SensorKind, Threshold>(), options));
    }

    [Fact]
    public void Thresholds_AreCopied_SoLaterChangesByTheCallerDoNothing()
    {
        var source = new FakeSensorSource { Readings = [Cpu(95)] };
        var thresholds = new Dictionary<SensorKind, Threshold> { [SensorKind.CpuTemp] = new(80, 90) };
        var monitor = new SensorMonitor(source, thresholds, new MonitorOptions { ConfirmReadings = 1 });
        var alerts = new List<SensorEvent>();
        monitor.AlertRaised += (_, e) => alerts.Add(e);

        thresholds.Clear();
        monitor.PollOnce();

        Assert.Single(alerts);
    }
}

using Udjat.Core.Alerts;
using Udjat.Core.Monitoring;
using Udjat.Core.Protection;
using Udjat.Core.Sensors;
using Udjat.Core.Settings;
using Xunit;

namespace Udjat.Tests;

/// <summary>Tests for <see cref="AutoProtector"/> (the wiring) and for the wording of its message.</summary>
public class AutoProtectTests
{
    private static readonly DateTimeOffset Time = new(2026, 10, 29, 18, 3, 12, TimeSpan.Zero);

    private static SensorEvent Hot() =>
        new(new SensorReading("gpu/hotspot", SensorKind.GpuHotspot, "NVIDIA GeForce RTX 3060", 109), AlertLevel.Critical, Time);

    private static AppSettings Settings(bool enabled, params string[] names) => new()
    {
        Advanced = new AdvancedSettings { ConfirmReadings = 1 },
        AutoProtect = new AutoProtectSettings { Enabled = enabled, AfterSeconds = 6, ProcessNames = [.. names] },
    };

    private sealed class Wiring
    {
        public Wiring(AppSettings settings, FakeProcessTable table)
        {
            Table = table;
            Monitor = new SensorMonitor(Source, settings.ToThresholds(), settings.ToMonitorOptions(), Clock);
            new AutoProtector(settings, new ProcessGuard(table, 999), Messages.Add).Attach(Monitor);
        }

        public FakeSensorSource Source { get; } = new();
        public FakeTimeProvider Clock { get; } = new(Harness.Start);
        public SensorMonitor Monitor { get; }
        public FakeProcessTable Table { get; }
        public List<AlertMessage> Messages { get; } = [];

        public void PollCpu(double temperature)
        {
            Source.Readings = [Readings.Cpu(temperature)];
            Clock.Advance(TimeSpan.FromSeconds(2));
            Monitor.PollOnce();
        }
    }

    // --- The wiring -------------------------------------------------------------------

    [Fact]
    public void AfterTheConfiguredTimeAtCritical_TheListedProgramsAreClosed_AndTheUserIsTold()
    {
        var table = new FakeProcessTable(new ProcessInfo(10, "ollama"), new ProcessInfo(30, "chrome"));
        var wiring = new Wiring(Settings(true, "ollama"), table);

        wiring.PollCpu(98);  // t = 2 s: Critical begins
        wiring.PollCpu(98);
        wiring.PollCpu(98);
        Assert.Empty(table.Killed);                      // 4 s so far, not enough
        wiring.PollCpu(98);                              // t = 8 s: 6 s of Critical

        Assert.Equal([10], table.Killed.Select(p => p.Id));
        var message = Assert.Single(wiring.Messages);
        Assert.Equal(AlertMessageKind.AutoProtect, message.Kind);
        Assert.Equal("Udjat auto-protect closed ollama.exe (1 process)", message.Title);
    }

    [Fact]
    public void ItActsOnce_PerCriticalStay()
    {
        var table = new FakeProcessTable(new ProcessInfo(10, "ollama"));
        var wiring = new Wiring(Settings(true, "ollama"), table);

        for (var i = 0; i < 12; i++)
            wiring.PollCpu(98);

        Assert.Single(wiring.Messages);
        Assert.Single(table.Killed);
    }

    [Fact]
    public void WhenItIsOff_NothingIsClosed_AndNothingIsSaid()
    {
        var table = new FakeProcessTable(new ProcessInfo(10, "ollama"));
        var wiring = new Wiring(Settings(false, "ollama"), table);

        for (var i = 0; i < 12; i++)
            wiring.PollCpu(98);

        Assert.Empty(table.Killed);
        Assert.Empty(wiring.Messages);
    }

    [Fact]
    public void ACriticalThatEndsEarly_ClosesNothing()
    {
        var table = new FakeProcessTable(new ProcessInfo(10, "ollama"));
        var wiring = new Wiring(Settings(true, "ollama"), table);

        wiring.PollCpu(98);
        wiring.PollCpu(98);
        wiring.PollCpu(50);  // cooled down after 2 s
        wiring.PollCpu(50);

        Assert.Empty(table.Killed);
        Assert.Empty(wiring.Messages);
    }

    [Fact]
    public async Task TheMessage_ReachesTheChannels_ThroughTheDispatcher()
    {
        var settings = Settings(true, "ollama");
        using var rig = new DispatcherRig(settings with { Email = new EmailSettings { Enabled = true } });
        var table = new FakeProcessTable(new ProcessInfo(10, "ollama"));
        new AutoProtector(settings, new ProcessGuard(table, 999), rig.Dispatcher.Enqueue).Attach(rig.Monitor);

        for (var i = 0; i < 6; i++)
            rig.PollCpu(98);
        await rig.DrainAsync();

        Assert.Equal([10], table.Killed.Select(p => p.Id));
        Assert.Contains(rig.Email.Sent, m => m.Kind == AlertMessageKind.AutoProtect);
        Assert.Contains(rig.LogLines(), line => line.Contains(" | AUTO-PROTECT | CPU temperature | CPU Package | "));
    }

    // --- The wording ------------------------------------------------------------------

    private static ProtectResult Result(params ProcessOutcome[] outcomes) => new(outcomes);

    [Fact]
    public void Message_SaysWhatWasClosed_AndWhy()
    {
        var result = Result(
            new ProcessOutcome("ollama", 10, ProcessStatus.Closed),
            new ProcessOutcome("ollama", 11, ProcessStatus.Closed),
            new ProcessOutcome("LM Studio", 20, ProcessStatus.Closed));

        var message = AlertText.AutoProtect(Hot(), result, TimeSpan.FromSeconds(60));

        Assert.Equal("Udjat auto-protect closed ollama.exe (2 processes), LM Studio.exe (1 process)", message.Title);
        Assert.StartsWith(message.Title + " because the GPU hotspot of NVIDIA GeForce RTX 3060 stayed critical for 60 s.", message.Body);
        Assert.EndsWith($"{AlertText.When(Time)}.", message.Body);
        Assert.Equal(AlertLevel.Critical, message.Level);
        Assert.Equal("AUTO-PROTECT", message.Label);
        Assert.Equal("109 °C", message.Value);
    }

    [Fact]
    public void Message_ListsWhatCouldNotBeClosed_AndWhatWasSkipped()
    {
        var result = Result(
            new ProcessOutcome("ollama", 10, ProcessStatus.Closed),
            new ProcessOutcome("LM Studio", 20, ProcessStatus.Failed, "UnauthorizedAccessException: Access is denied."),
            new ProcessOutcome("svchost", 30, ProcessStatus.Protected),
            new ProcessOutcome("svchost", 31, ProcessStatus.Protected));

        var message = AlertText.AutoProtect(Hot(), result, TimeSpan.FromSeconds(60));

        Assert.Contains("Could not close: LM Studio.exe (UnauthorizedAccessException: Access is denied.).", message.Body);
        Assert.Contains("Skipped as protected: svchost.exe.", message.Body);
    }

    [Fact]
    public void Message_SaysWhichProgramsWereLeftAloneBecauseTheyBelongToSomeoneElse()
    {
        var result = Result(
            new ProcessOutcome("ollama", 10, ProcessStatus.Closed),
            new ProcessOutcome("backup", 40, ProcessStatus.OtherUser),
            new ProcessOutcome("backup", 41, ProcessStatus.OtherUser));

        var message = AlertText.AutoProtect(Hot(), result, TimeSpan.FromSeconds(60));

        Assert.Contains("Left alone because they run under another account or session: backup.exe.", message.Body);
    }

    [Fact]
    public void Message_WhenNothingCouldBeClosed_SaysSo()
    {
        var failed = Result(new ProcessOutcome("ollama", 10, ProcessStatus.Failed, "denied"));

        Assert.Equal("Udjat auto-protect could not close anything", AlertText.AutoProtect(Hot(), failed, TimeSpan.FromSeconds(60)).Title);
    }

    [Fact]
    public void Message_WhenNothingMatched_SaysThereWasNothingToClose()
    {
        var message = AlertText.AutoProtect(Hot(), Result(), TimeSpan.FromSeconds(60));

        Assert.Equal("Udjat auto-protect found nothing to close", message.Title);
        Assert.Contains("stayed critical for 60 s.", message.Body);
    }
}

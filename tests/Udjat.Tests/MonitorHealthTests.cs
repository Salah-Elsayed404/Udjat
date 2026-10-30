using Udjat.Core.Alerts;
using Xunit;

namespace Udjat.Tests;

/// <summary>Tests for <see cref="MonitorHealth"/>, driven by the fake clock (its timer fires as the clock advances).</summary>
public class MonitorHealthTests
{
    private static readonly TimeSpan StopAfter = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(15);

    private static (MonitorHealth Health, FakeTimeProvider Clock, List<DateTimeOffset> Stopped, List<DateTimeOffset> Resumed) Make()
    {
        var clock = new FakeTimeProvider(Harness.Start);
        var health = new MonitorHealth(StopAfter, CheckEvery, clock);
        var stopped = new List<DateTimeOffset>();
        var resumed = new List<DateTimeOffset>();
        health.Stopped += (_, since) => stopped.Add(since);
        health.Resumed += (_, now) => resumed.Add(now);
        return (health, clock, stopped, resumed);
    }

    [Fact]
    public void NoPollAtAll_CountsAsStopped_AfterTheLimit_Once()
    {
        var (health, clock, stopped, _) = Make();
        using var _ = health;

        clock.Advance(TimeSpan.FromSeconds(45));
        Assert.Empty(stopped);

        clock.Advance(TimeSpan.FromSeconds(15));  // 60 s
        clock.Advance(TimeSpan.FromMinutes(5));   // still stopped, but said only once

        Assert.Equal([Harness.Start], stopped);
    }

    [Fact]
    public void RegularPolls_KeepItQuiet()
    {
        var (health, clock, stopped, resumed) = Make();
        using var _ = health;

        for (var i = 0; i < 60; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(2));
            health.OnPolled();
        }

        Assert.Empty(stopped);
        Assert.Empty(resumed);
    }

    [Fact]
    public void StoppedThenPolling_SaysResumed_Once_AndCanStopAgain()
    {
        var (health, clock, stopped, resumed) = Make();
        using var _ = health;
        clock.Advance(TimeSpan.FromSeconds(60));  // stopped

        clock.Advance(TimeSpan.FromSeconds(10));
        health.OnPolled();
        health.OnPolled();
        var resumedAt = clock.GetUtcNow();

        Assert.Equal([resumedAt], resumed);

        clock.Advance(TimeSpan.FromSeconds(75));  // and silent again for too long
        Assert.Equal(2, stopped.Count);
        Assert.Equal(resumedAt, stopped[1]);      // "since" is the last successful poll
    }

    [Fact]
    public void ThePollIsWhatCounts_NotTheCheck()
    {
        var (health, clock, stopped, _) = Make();
        using var _ = health;
        clock.Advance(TimeSpan.FromSeconds(45));
        health.OnPolled();

        clock.Advance(TimeSpan.FromSeconds(45));  // 45 s since the poll, 90 s since the start

        Assert.Empty(stopped);
    }

    [Fact]
    public void Dispose_StopsTheTimer()
    {
        var (health, clock, stopped, _) = Make();
        health.Dispose();

        clock.Advance(TimeSpan.FromMinutes(10));

        Assert.Empty(stopped);
    }

    [Fact]
    public void AHandlerThatThrows_DoesNotEscape_NorBlockTheNextOne()
    {
        var (health, clock, stopped, _) = Make();
        using var _ = health;
        health.Stopped += (_, _) => throw new InvalidOperationException("handler bug");
        var second = 0;
        health.Stopped += (_, _) => second++;

        clock.Advance(TimeSpan.FromSeconds(60));  // the timer would crash the process if this escaped

        Assert.Single(stopped);
        Assert.Equal(1, second);
    }

    [Fact]
    public void BadTimes_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => new MonitorHealth(TimeSpan.Zero, CheckEvery));
        Assert.Throws<ArgumentException>(() => new MonitorHealth(StopAfter, TimeSpan.FromSeconds(-1)));
        Assert.Throws<ArgumentException>(() => new MonitorHealth(StopAfter, StopAfter));  // it would never look in time
    }

    [Fact]
    public void ASleepingPc_IsNotBlamedOnTheMonitor()
    {
        var (health, clock, stopped, resumed) = Make();
        health.Dispose();  // no timer: the PC was asleep, so the timer never ran

        clock.Advance(TimeSpan.FromMinutes(30));
        health.Check();  // the first look after waking up

        Assert.Empty(stopped);
        Assert.Empty(resumed);
    }

    [Fact]
    public void AfterASleep_AMonitorThatReallyStopped_IsStillCaught()
    {
        var (health, clock, stopped, _) = Make();
        health.Dispose();
        clock.Advance(TimeSpan.FromMinutes(30));
        health.Check();  // fresh start

        for (var i = 0; i < 4; i++)  // then 60 s of looking every 15 s, with no poll
        {
            clock.Advance(CheckEvery);
            health.Check();
        }

        Assert.Single(stopped);
    }

    [Fact]
    public void Attached_ToARealMonitor_CountsItsSuccessfulPollsOnly()
    {
        using var rig = new DispatcherRig();
        using var health = new MonitorHealth(StopAfter, CheckEvery, rig.Clock);
        health.Attach(rig.Monitor);
        var stopped = new List<DateTimeOffset>();
        health.Stopped += (_, since) => stopped.Add(since);

        rig.Source.Failure = new InvalidOperationException("driver gone");  // PollFailed, never Polled
        for (var i = 0; i < 40; i++)
            rig.PollCpu(50);

        Assert.NotEmpty(stopped);
    }

    [Fact]
    public async Task WithTheDispatcher_StoppedAndResumedBecomeMessages()
    {
        using var rig = new DispatcherRig();
        using var health = new MonitorHealth(StopAfter, CheckEvery, rig.Clock);
        health.Attach(rig.Monitor);
        rig.Dispatcher.Attach(health);

        rig.Clock.Advance(TimeSpan.FromSeconds(60));  // no polls: stopped
        rig.PollCpu(50);                               // working again
        await rig.DrainAsync();

        Assert.Equal(["Udjat cannot read the sensors", "Udjat is reading the sensors again."],
            rig.Telegram.Sent.Select(m => m.Title.StartsWith("Udjat cannot read the sensors") ? "Udjat cannot read the sensors" : m.Title));
        Assert.Empty(rig.Sound.Sent);  // not an alarm
        Assert.Contains(" | STOPPED | ", rig.LogLines()[0]);
        Assert.Contains(" | RESUMED | ", rig.LogLines()[1]);
    }
}

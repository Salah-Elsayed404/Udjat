using Udjat.Core.Monitoring;
using Xunit;

namespace Udjat.Tests;

/// <summary>
/// Tests for the cooldown, the "recovered" notice and sustained Critical (auto-protect).
/// Cooldown is 10 minutes and sustained Critical is 60 s, unless a test says otherwise.
/// See <see cref="StateRun"/> for how readings are fed in.
/// </summary>
public class ThresholdStateAlertTests
{
    // --- Cooldown and "recovered" -------------------------------------------

    [Fact]
    public void Cooldown_SilencesRepeatAlert_AndItsRecovery()
    {
        var run = new StateRun();
        run.FeedTwice(85);
        run.Feed(70);                          // recovered (an alert had been sent)

        var again = run.FeedTwice(85);         // seconds later: inside the cooldown
        var recovery = run.Feed(70);

        Assert.Equal(AlertLevel.Warning, again.Current);
        Assert.False(again.SendAlert);
        Assert.False(recovery.SendRecovered);  // the user never heard about this episode
    }

    [Fact]
    public void AfterCooldown_AlertsAgain()
    {
        var run = new StateRun();
        run.FeedTwice(85);
        run.Feed(70);

        run.Feed(85, secondsLater: 11 * 60);
        var again = run.Feed(85);

        Assert.True(again.SendAlert);
    }

    [Fact]
    public void RecentWarningAlert_DoesNotSilenceCritical()
    {
        var run = new StateRun();
        run.FeedTwice(85);

        var critical = run.FeedTwice(95);

        Assert.Equal(AlertLevel.Critical, critical.Current);
        Assert.True(critical.SendAlert);
    }

    [Fact]
    public void CriticalCooldown_AppliesWhenBouncingBackToCritical()
    {
        var run = new StateRun();
        run.FeedTwice(95);                    // Critical alert
        run.Feed(86);                         // down to Warning

        var again = run.FeedTwice(95);        // back to Critical, still inside the cooldown

        Assert.Equal(AlertLevel.Critical, again.Current);
        Assert.False(again.SendAlert);
    }

    // --- Sustained Critical (auto-protect) ----------------------------------

    [Fact]
    public void CriticalSustained_FiresOnce_AfterTheConfiguredTime()
    {
        var run = new StateRun();
        run.FeedTwice(95);

        var early = run.Feed(95, secondsLater: 30);
        var due = run.Feed(95, secondsLater: 30);
        var after = run.Feed(95, secondsLater: 30);

        Assert.False(early.CriticalSustained);
        Assert.True(due.CriticalSustained);
        Assert.False(after.CriticalSustained);
    }

    [Fact]
    public void CriticalSustained_IsTimedFromEnteringCritical_NotFromWarning()
    {
        var run = new StateRun();
        run.FeedTwice(85);
        run.Feed(85, secondsLater: 300);      // a long time at Warning

        var entered = run.FeedTwice(95);
        var justBefore = run.Feed(95, secondsLater: 59);
        var due = run.Feed(95, secondsLater: 1);

        Assert.False(entered.CriticalSustained);
        Assert.False(justBefore.CriticalSustained);
        Assert.True(due.CriticalSustained);
    }

    [Fact]
    public void CriticalSustained_HoldsThroughHysteresisDips()
    {
        var run = new StateRun();
        run.FeedTwice(95);

        run.Feed(88, secondsLater: 30);       // dip, but still Critical
        var due = run.Feed(95, secondsLater: 30);

        Assert.True(due.CriticalSustained);
    }

    [Fact]
    public void CriticalSustained_DoesNotFire_IfCriticalEndsFirst()
    {
        var run = new StateRun();
        run.FeedTwice(95);
        run.Feed(50, secondsLater: 30);       // the stay ends after 30 s

        run.FeedTwice(95);                    // a new stay starts
        var update = run.Feed(95, secondsLater: 30);

        Assert.False(update.CriticalSustained);
    }

    [Fact]
    public void CriticalSustained_CanFireAgain_InALaterStay()
    {
        var run = new StateRun();
        run.FeedTwice(95);
        Assert.True(run.Feed(95, secondsLater: 60).CriticalSustained);
        run.Feed(50);

        run.FeedTwice(95);
        var second = run.Feed(95, secondsLater: 60);

        Assert.True(second.CriticalSustained);
    }

    [Fact]
    public void CriticalSustained_WithZeroTime_FiresWhenCriticalBegins()
    {
        var run = new StateRun(new MonitorOptions { SustainedCriticalAfter = TimeSpan.Zero });

        var update = run.FeedTwice(95);

        Assert.True(update.CriticalSustained);
    }
}

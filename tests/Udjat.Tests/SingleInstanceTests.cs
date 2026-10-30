using Udjat.Windows;
using Xunit;

namespace Udjat.Tests;

/// <summary>Tests for <see cref="SingleInstance"/>. Each test uses its own object names, so it never meets a real Udjat.</summary>
public class SingleInstanceTests
{
    private static string NewName() => @"Local\UdjatTest-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void TheFirstStartBecomesTheInstance_AndALaterOneIsToldSo()
    {
        var name = NewName();

        using var first = SingleInstance.TryBecomeFirst(name);
        var second = SingleInstance.TryBecomeFirst(name);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public void AfterTheFirstIsGone_TheNextStartIsFirstAgain()
    {
        var name = NewName();
        SingleInstance.TryBecomeFirst(name)!.Dispose();

        using var next = SingleInstance.TryBecomeFirst(name);

        Assert.NotNull(next);
    }

    [Fact]
    public void ALaterStart_CanAskTheFirstToShowItself_OrToClose()
    {
        var name = NewName();
        using var first = SingleInstance.TryBecomeFirst(name)!;
        using var shown = new ManualResetEventSlim();
        using var closed = new ManualResetEventSlim();
        first.ShowRequested += shown.Set;
        first.ExitRequested += closed.Set;

        SingleInstance.Signal(SingleInstance.ShowEventName(name));
        Assert.True(shown.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken), "the first instance should have been asked to show itself");
        Assert.False(closed.IsSet);

        SingleInstance.Signal(SingleInstance.ExitEventName(name));
        Assert.True(closed.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken), "the first instance should have been asked to close");
    }

    [Fact]
    public void SignallingWhenNothingRuns_DoesNothing()
    {
        SingleInstance.Signal(SingleInstance.ShowEventName(NewName()));  // no exception
    }

    [Fact]
    public void AMutexSomeoneElseMadeFirst_IsReported_NotMistakenForARunningUdjat()
    {
        var name = NewName();
        using var squatter = new Mutex(true, name + ".SingleInstance");

        var error = Assert.Throws<InvalidOperationException>(() => SingleInstance.TryBecomeFirst(name));

        Assert.Contains(name, error.Message);
    }

    [Fact]
    public void AnEventSomeoneElseMadeFirst_IsReported_SoItCannotQuietlyStopUdjat()
    {
        var name = NewName();
        using var squatter = new EventWaitHandle(false, EventResetMode.AutoReset, SingleInstance.ExitEventName(name));

        Assert.Throws<InvalidOperationException>(() => SingleInstance.TryBecomeFirst(name));

        // Nothing is left half made: once the squatter is gone, the next start works.
        squatter.Dispose();
        using var next = SingleInstance.TryBecomeFirst(name);
        Assert.NotNull(next);
    }
}

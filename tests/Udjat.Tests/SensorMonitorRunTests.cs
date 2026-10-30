using Xunit;
using static Udjat.Tests.Harness;
using static Udjat.Tests.Readings;

namespace Udjat.Tests;

/// <summary>Tests for <c>SensorMonitor.RunAsync</c>, driven by a fake clock.</summary>
public class SensorMonitorRunTests
{
    /// <summary>Waits until <paramref name="release"/> has been signalled, failing the test after 5 s.</summary>
    private static async Task WaitAsync(SemaphoreSlim release) =>
        Assert.True(await release.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken), "timed out waiting for a poll");

    [Fact]
    public async Task RunAsync_PollsAtOnce_ThenEveryInterval_UntilCancelled()
    {
        var h = new Harness();
        h.Source.Readings = [Cpu(50)];
        var polled = new SemaphoreSlim(0);
        h.Monitor.Polled += (_, _) => polled.Release();
        using var cts = new CancellationTokenSource();

        var run = h.Monitor.RunAsync(cts.Token);
        await WaitAsync(polled);                 // the poll at start-up
        Assert.Equal(1, h.Source.ReadCount);

        h.Clock.Advance(Interval);
        await WaitAsync(polled);
        h.Clock.Advance(Interval);
        await WaitAsync(polled);
        Assert.Equal(3, h.Source.ReadCount);

        await cts.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(run.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task RunAsync_KeepsRunning_WhenTheSourceKeepsFailing()
    {
        var h = new Harness();
        h.Source.Failure = new InvalidOperationException("sensor driver gone");
        var failed = new SemaphoreSlim(0);
        h.Monitor.PollFailed += (_, _) => failed.Release();
        using var cts = new CancellationTokenSource();

        var run = h.Monitor.RunAsync(cts.Token);
        await WaitAsync(failed);
        h.Clock.Advance(Interval);
        await WaitAsync(failed);                 // still polling after the first failure

        Assert.False(run.IsCompleted);
        await cts.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RunAsync_WithAlreadyCancelledToken_DoesNotPoll()
    {
        var h = new Harness();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await h.Monitor.RunAsync(cts.Token);

        Assert.Equal(0, h.Source.ReadCount);
    }
}

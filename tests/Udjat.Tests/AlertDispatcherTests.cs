using Udjat.Core.Alerts;
using Udjat.Core.Monitoring;
using Udjat.Core.Settings;
using Xunit;

namespace Udjat.Tests;

/// <summary>Tests for <see cref="AlertDispatcher"/>: who hears what, what happens when a channel fails, and the log.</summary>
public class AlertDispatcherTests
{
    // --- Who hears what -----------------------------------------------------------

    [Fact]
    public async Task Warning_GoesToTheToastOnly()
    {
        using var rig = new DispatcherRig();

        rig.PollCpu(91);
        await rig.DrainAsync();

        var message = Assert.Single(rig.Toast.Sent);
        Assert.Equal("Udjat: CPU temperature warning (91 °C)", message.Title);
        Assert.Empty(rig.Sound.Sent);
        Assert.Empty(rig.Email.Sent);
        Assert.Empty(rig.Telegram.Sent);
    }

    [Fact]
    public async Task Critical_GoesToEveryChannel_AndIsLogged()
    {
        using var rig = new DispatcherRig();

        rig.PollCpu(98);
        await rig.DrainAsync();

        foreach (var channel in rig.Channels.Values)
            Assert.Equal("Udjat: CPU temperature is CRITICAL (98 °C)", Assert.Single(channel.Sent).Title);

        var line = Assert.Single(rig.LogLines());
        Assert.EndsWith(" | CRITICAL | CPU temperature | CPU Package | 98 °C | toast: sent, sound: sent, email: sent, telegram: sent", line);
    }

    [Fact]
    public async Task Recovered_GoesToTheChannelsThatHeardTheAlert_ExceptSound()
    {
        using var rig = new DispatcherRig();
        rig.PollCpu(98);

        rig.PollCpu(60);
        await rig.DrainAsync();

        Assert.Equal(2, rig.Toast.Sent.Count);
        Assert.Equal(2, rig.Email.Sent.Count);
        Assert.Equal(2, rig.Telegram.Sent.Count);
        Assert.Single(rig.Sound.Sent);  // only the alarm itself
        Assert.Equal(AlertMessageKind.Recovered, rig.Telegram.Sent[1].Kind);
    }

    [Fact]
    public async Task Recovered_FollowsTheHighestAlertOfTheEpisode()
    {
        using var rig = new DispatcherRig();
        rig.PollCpu(92);   // Warning: toast only
        rig.PollCpu(98);   // Critical: everything
        rig.PollCpu(60);   // over

        await rig.DrainAsync();

        Assert.Equal(AlertMessageKind.Recovered, rig.Telegram.Sent[^1].Kind);
        Assert.Equal(2, rig.Telegram.Sent.Count);
    }

    [Fact]
    public async Task Recovered_AfterAWarningOnly_GoesToTheToastOnly()
    {
        using var rig = new DispatcherRig();
        rig.PollCpu(92);

        rig.PollCpu(60);
        await rig.DrainAsync();

        Assert.Equal(2, rig.Toast.Sent.Count);
        Assert.Empty(rig.Telegram.Sent);
        Assert.Empty(rig.Email.Sent);
    }

    [Fact]
    public async Task AnAlertSilencedByTheCooldown_HasNoRecoveredNotice()
    {
        using var rig = new DispatcherRig();
        rig.PollCpu(98);   // alert
        rig.PollCpu(60);   // recovered
        rig.PollCpu(98);   // again, inside the cooldown: silent
        rig.PollCpu(60);   // over, and nobody was told about this one

        await rig.DrainAsync();

        Assert.Equal(2, rig.Telegram.Sent.Count);  // one alert, one recovered
    }

    [Fact]
    public async Task ASwitchedOffChannel_IsNeverCalled()
    {
        var settings = new AppSettings { Advanced = new AdvancedSettings { ConfirmReadings = 1 } };  // email and Telegram are off
        using var rig = new DispatcherRig(settings);

        rig.PollCpu(98);
        await rig.DrainAsync();

        Assert.Empty(rig.Email.Sent);
        Assert.Empty(rig.Telegram.Sent);
        Assert.Single(rig.Toast.Sent);
        Assert.EndsWith("toast: sent, sound: sent", Assert.Single(rig.LogLines()));
    }

    [Fact]
    public async Task WithNoChannelAtAll_TheLogStillSaysSo()
    {
        var settings = new AppSettings { Toast = new ChannelToggle { Enabled = false }, Advanced = new AdvancedSettings { ConfirmReadings = 1 } };
        using var rig = new DispatcherRig(settings);

        rig.PollCpu(92);
        await rig.DrainAsync();

        Assert.EndsWith(" | WARNING | CPU temperature | CPU Package | 92 °C | no channel", Assert.Single(rig.LogLines()));
    }

    [Fact]
    public async Task AChannelThatIsOnButNotBuilt_IsReportedAsUnavailable()
    {
        using var rig = new DispatcherRig(register: [ChannelKind.Toast, ChannelKind.Sound]);  // no email or Telegram channel

        rig.PollCpu(98);
        await rig.DrainAsync();

        var line = Assert.Single(rig.LogLines());
        Assert.EndsWith("toast: sent, sound: sent, email: failed (channel not available), telegram: failed (channel not available)", line);
    }

    [Fact]
    public async Task MessagesFromOutside_AreRoutedByKind()
    {
        using var rig = new DispatcherRig();
        var message = new AlertMessage(AlertMessageKind.AutoProtect, AlertLevel.Critical, "Udjat auto-protect closed ollama.exe", "details", rig.Clock.GetUtcNow());

        rig.Dispatcher.Enqueue(message);
        await rig.DrainAsync();

        Assert.Same(message, Assert.Single(rig.Toast.Sent));
        Assert.Single(rig.Email.Sent);
        Assert.Single(rig.Telegram.Sent);
        Assert.Empty(rig.Sound.Sent);  // an alarm is only for alerts
        Assert.Contains(" | AUTO-PROTECT | ", Assert.Single(rig.LogLines()));
    }

    // --- When a channel fails --------------------------------------------------------

    [Fact]
    public async Task AFailingChannel_DoesNotStopTheOthers_AndItsSecretsAreHidden()
    {
        using var rig = new DispatcherRig(secrets: ["hunter2"]);
        rig.Email.Behavior = (_, _) => throw new InvalidOperationException("login failed for hunter2\r\nsecond line");

        rig.PollCpu(98);
        await rig.DrainAsync();

        Assert.Single(rig.Toast.Sent);
        Assert.Single(rig.Telegram.Sent);
        Assert.Empty(rig.Email.Sent);

        var line = Assert.Single(rig.LogLines());
        Assert.DoesNotContain("hunter2", line);
        Assert.Contains("email: failed (InvalidOperationException: login failed for *** second line), telegram: sent", line);
    }

    [Fact]
    public async Task AChannelThatHangs_TimesOut_AndTheRestStillGetTheMessage()
    {
        using var rig = new DispatcherRig(sendTimeout: TimeSpan.FromMilliseconds(50));
        rig.Telegram.Behavior = async (_, token) => await Task.Delay(Timeout.Infinite, token);

        rig.PollCpu(98);
        await rig.DrainAsync();

        Assert.Single(rig.Toast.Sent);
        Assert.Single(rig.Email.Sent);
        Assert.Empty(rig.Telegram.Sent);
        Assert.EndsWith("email: sent, telegram: failed (timed out)", Assert.Single(rig.LogLines()));
    }

    [Fact]
    public async Task ALongErrorMessage_IsCutShort()
    {
        using var rig = new DispatcherRig();
        rig.Toast.Behavior = (_, _) => throw new InvalidOperationException(new string('x', 500));

        rig.PollCpu(92);
        await rig.DrainAsync();

        var line = Assert.Single(rig.LogLines());
        Assert.True(line.Length < 250);
        Assert.Contains("…", line);
    }

    [Fact]
    public async Task ASlowChannel_NeverBlocksThePollLoop()
    {
        using var rig = new DispatcherRig();
        var release = new TaskCompletionSource();
        rig.Toast.Behavior = async (_, _) => await release.Task;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var consumer = rig.Dispatcher.RunAsync(cts.Token);

        rig.PollCpu(92);  // returns at once although the toast is stuck
        rig.PollCpu(60);

        Assert.Empty(rig.Toast.Sent);
        release.SetResult();
        rig.Dispatcher.Complete();
        await consumer.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(2, rig.Toast.Sent.Count);
    }

    [Fact]
    public async Task Cancelling_EndsTheConsumerNormally()
    {
        using var rig = new DispatcherRig();
        using var cts = new CancellationTokenSource();
        var consumer = rig.Dispatcher.RunAsync(cts.Token);

        await cts.CancelAsync();

        await consumer.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(consumer.IsCompletedSuccessfully);
    }

    // --- The "Test" button -----------------------------------------------------------

    [Fact]
    public async Task SendTest_ReachesOnlyThatChannel_AndReportsSuccess()
    {
        using var rig = new DispatcherRig();

        var result = await rig.Dispatcher.SendTestAsync(ChannelKind.Telegram, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal("sent", result.Detail);
        Assert.Equal("Udjat: test message", Assert.Single(rig.Telegram.Sent).Title);
        Assert.Empty(rig.Toast.Sent);
        Assert.Empty(rig.LogLines());  // tests are not logged
    }

    [Fact]
    public async Task SendTest_ReportsWhyItFailed_WithoutSecrets()
    {
        using var rig = new DispatcherRig(secrets: ["hunter2"]);
        rig.Email.Behavior = (_, _) => throw new InvalidOperationException("bad password hunter2");

        var result = await rig.Dispatcher.SendTestAsync(ChannelKind.Email, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("InvalidOperationException: bad password ***", result.Detail);
    }

    [Fact]
    public async Task SendTest_ToAChannelThatDoesNotExist_SaysSo()
    {
        using var rig = new DispatcherRig(register: [ChannelKind.Toast]);

        var result = await rig.Dispatcher.SendTestAsync(ChannelKind.Email, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("channel not available", result.Detail);
    }
}

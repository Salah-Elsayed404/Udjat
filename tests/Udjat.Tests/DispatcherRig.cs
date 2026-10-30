using Udjat.Core.Alerts;
using Udjat.Core.Logging;
using Udjat.Core.Monitoring;
using Udjat.Core.Settings;
using Xunit;

namespace Udjat.Tests;

/// <summary>A channel that only records what it was given. <see cref="Behavior"/> can make it fail or hang.</summary>
internal sealed class RecordingChannel(ChannelKind kind) : IAlertChannel
{
    public ChannelKind Kind => kind;

    public List<AlertMessage> Sent { get; } = [];

    /// <summary>Runs before the message counts as sent; throw to fail, or wait on the token to hang.</summary>
    public Func<AlertMessage, CancellationToken, Task>? Behavior { get; set; }

    public async Task SendAsync(AlertMessage message, CancellationToken cancellationToken)
    {
        if (Behavior is not null)
            await Behavior(message, cancellationToken);
        Sent.Add(message);
    }
}

/// <summary>
/// A real <see cref="SensorMonitor"/> and <see cref="AlertDispatcher"/> wired together with fake sensors,
/// a fake clock and recording channels. Default settings, except email and Telegram are on and one reading confirms a level.
/// The CPU thresholds are the defaults: Warning 90, Critical 97.
/// </summary>
internal sealed class DispatcherRig : IDisposable
{
    private readonly TempFolder _folder = new();

    public DispatcherRig(
        AppSettings? settings = null,
        TimeSpan? sendTimeout = null,
        IEnumerable<string>? secrets = null,
        IEnumerable<ChannelKind>? register = null)
    {
        Settings = settings ?? new AppSettings
        {
            Email = new EmailSettings { Enabled = true },
            Telegram = new TelegramSettings { Enabled = true },
            Advanced = new AdvancedSettings { ConfirmReadings = 1 },
        };

        Channels = (register ?? Enum.GetValues<ChannelKind>()).ToDictionary(kind => kind, kind => new RecordingChannel(kind));
        Monitor = new SensorMonitor(Source, Settings.ToThresholds(), Settings.ToMonitorOptions(), Clock);
        Dispatcher = new AlertDispatcher(Settings, Channels.Values, new AlertLog(LogPath), secrets ?? [], Clock, sendTimeout);
        Dispatcher.Attach(Monitor);
    }

    public AppSettings Settings { get; }
    public FakeSensorSource Source { get; } = new();
    public FakeTimeProvider Clock { get; } = new(Harness.Start);
    public SensorMonitor Monitor { get; }
    public AlertDispatcher Dispatcher { get; }
    public Dictionary<ChannelKind, RecordingChannel> Channels { get; }

    public string LogPath => _folder.File("alerts.log");

    public RecordingChannel Toast => Channels[ChannelKind.Toast];
    public RecordingChannel Sound => Channels[ChannelKind.Sound];
    public RecordingChannel Email => Channels[ChannelKind.Email];
    public RecordingChannel Telegram => Channels[ChannelKind.Telegram];

    /// <summary>Lets two seconds pass, then polls once with a CPU temperature.</summary>
    public void PollCpu(double temperature)
    {
        Source.Readings = [Readings.Cpu(temperature)];
        Clock.Advance(TimeSpan.FromSeconds(2));
        Monitor.PollOnce();
    }

    /// <summary>Stops accepting messages and delivers everything queued.</summary>
    public async Task DrainAsync()
    {
        Dispatcher.Complete();
        await Dispatcher.RunAsync(TestContext.Current.CancellationToken);
    }

    public string[] LogLines() => File.Exists(LogPath) ? File.ReadAllLines(LogPath) : [];

    public void Dispose() => _folder.Dispose();
}

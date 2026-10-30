using System.Threading.Channels;
using Udjat.Core.Logging;
using Udjat.Core.Monitoring;
using Udjat.Core.Sensors;
using Udjat.Core.Settings;

namespace Udjat.Core.Alerts;

/// <summary>
/// Turns the monitor's events into messages, decides who gets them, and delivers them in the background.
/// The event handlers only put a message in a queue, so the poll loop never waits on the network.
/// One consumer (<see cref="RunAsync"/>) sends each message to its channels one after the other,
/// with a time limit per channel, and writes the outcome to the alert log. There are no retries in v1.
/// Settings are fixed for the dispatcher's lifetime; when they change, build a new one.
/// </summary>
public sealed class AlertDispatcher
{
    private static readonly TimeSpan DefaultSendTimeout = TimeSpan.FromSeconds(30);
    private const int MaxReasonLength = 120;

    private readonly AppSettings _settings;
    private readonly IReadOnlyDictionary<SensorKind, Threshold> _thresholds;
    private readonly Dictionary<ChannelKind, IAlertChannel> _channels;
    private readonly AlertLog _log;
    private readonly string[] _secrets;
    private readonly TimeProvider _time;
    private readonly TimeSpan _sendTimeout;

    // If the network is down for a long time, the oldest messages are dropped rather than piling up.
    private readonly Channel<Queued> _queue = Channel.CreateBounded<Queued>(
        new BoundedChannelOptions(200) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    // The highest alert actually sent per sensor in the current episode; only touched on the polling thread.
    private readonly Dictionary<string, Episode> _episodes = new();

    private sealed record Queued(AlertMessage Message, IReadOnlyList<ChannelKind> Channels);

    private sealed record Episode(AlertLevel Level, IReadOnlyList<ChannelKind> Channels);

    /// <param name="secrets">Passwords and tokens, to be hidden if they turn up in an error message.</param>
    public AlertDispatcher(
        AppSettings settings,
        IEnumerable<IAlertChannel> channels,
        AlertLog log,
        IEnumerable<string> secrets,
        TimeProvider? timeProvider = null,
        TimeSpan? sendTimeout = null)
    {
        _settings = settings;
        _thresholds = settings.ToThresholds();
        _channels = channels.ToDictionary(channel => channel.Kind);
        _log = log;
        _secrets = secrets.ToArray();
        _time = timeProvider ?? TimeProvider.System;
        _sendTimeout = sendTimeout ?? DefaultSendTimeout;
    }

    /// <summary>Listens for alerts and recoveries.</summary>
    public void Attach(SensorMonitor monitor)
    {
        monitor.AlertRaised += OnAlert;
        monitor.Recovered += OnRecovered;
    }

    /// <summary>Listens for "monitoring stopped" and "resumed".</summary>
    public void Attach(MonitorHealth health)
    {
        health.Stopped += (_, since) => Enqueue(AlertText.MonitoringStopped(since, _time.GetUtcNow()));
        health.Resumed += (_, now) => Enqueue(AlertText.MonitoringResumed(now));
    }

    /// <summary>Queues a message for the channels the settings give it. Safe to call from any thread.</summary>
    public void Enqueue(AlertMessage message) => Enqueue(message, ChannelRouter.For(_settings, message));

    /// <summary>No more messages will come. <see cref="RunAsync"/> finishes what is queued and then returns.</summary>
    public void Complete() => _queue.Writer.TryComplete();

    /// <summary>Delivers queued messages until <see cref="Complete"/> or until cancelled. Cancelling ends it normally.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(cancellationToken))
                await DeliverAsync(item, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Stopping is the normal way to end.
        }
    }

    /// <summary>Sends a test message to one channel (the "Test" button) and says how it went. Not logged.</summary>
    public Task<DeliveryResult> SendTestAsync(ChannelKind channel, CancellationToken cancellationToken) =>
        SendAsync(channel, AlertText.Test(_time.GetUtcNow()), cancellationToken);

    private void OnAlert(object? sender, SensorEvent e)
    {
        var message = AlertText.Alert(e, LimitFor(e), _settings.TemperatureUnit);
        var channels = ChannelRouter.For(_settings, message);

        var id = e.Reading.Id;
        if (!_episodes.TryGetValue(id, out var episode) || e.Level >= episode.Level)
            _episodes[id] = new Episode(e.Level, channels);

        Enqueue(message, channels);
    }

    private void OnRecovered(object? sender, SensorEvent e)
    {
        var message = AlertText.Recovered(e, _settings.TemperatureUnit);
        IReadOnlyCollection<ChannelKind>? heard = _episodes.Remove(e.Reading.Id, out var episode) ? episode.Channels : null;

        Enqueue(message, ChannelRouter.For(_settings, message, heard));
    }

    private double LimitFor(SensorEvent e) =>
        _thresholds.TryGetValue(e.Reading.Kind, out var threshold)
            ? (e.Level == AlertLevel.Critical ? threshold.Critical : threshold.Warning)
            : e.Reading.Value;

    private void Enqueue(AlertMessage message, IReadOnlyList<ChannelKind> channels) =>
        _queue.Writer.TryWrite(new Queued(message, channels));

    private async Task DeliverAsync(Queued item, CancellationToken cancellationToken)
    {
        var outcomes = new List<string>();
        foreach (var channel in item.Channels)
        {
            var result = await SendAsync(channel, item.Message, cancellationToken);
            var name = channel.ToString().ToLowerInvariant();
            outcomes.Add(result.Success ? $"{name}: sent" : $"{name}: failed ({result.Detail})");
        }

        var message = item.Message;
        var delivery = outcomes.Count > 0 ? string.Join(", ", outcomes) : "no channel";
        _log.Append(message.Time, message.Label, message.Subject, message.Source, message.Value, delivery);
    }

    private async Task<DeliveryResult> SendAsync(ChannelKind kind, AlertMessage message, CancellationToken cancellationToken)
    {
        if (!_channels.TryGetValue(kind, out var channel))
            return new DeliveryResult(kind, false, "channel not available");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_sendTimeout);

        try
        {
            await channel.SendAsync(message, timeout.Token);
            return new DeliveryResult(kind, true, "sent");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;  // Udjat is shutting down
        }
        catch (OperationCanceledException)
        {
            return new DeliveryResult(kind, false, "timed out");
        }
        catch (Exception ex)
        {
            return new DeliveryResult(kind, false, Reason(ex));
        }
    }

    /// <summary>The exception type and message on one short line, with secrets hidden.</summary>
    private string Reason(Exception ex)
    {
        var text = Redactor.Redact($"{ex.GetType().Name}: {ex.Message}", _secrets)
            .Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');

        return text.Length <= MaxReasonLength ? text : text[..MaxReasonLength] + "…";
    }
}

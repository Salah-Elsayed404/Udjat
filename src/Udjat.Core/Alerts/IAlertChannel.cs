namespace Udjat.Core.Alerts;

/// <summary>One way of reaching the user. A channel reports a failure by throwing; the dispatcher deals with it.</summary>
public interface IAlertChannel
{
    ChannelKind Kind { get; }

    Task SendAsync(AlertMessage message, CancellationToken cancellationToken);
}

/// <summary>How one delivery went. <paramref name="Detail"/> is "sent", or a short reason with secrets removed.</summary>
public sealed record DeliveryResult(ChannelKind Channel, bool Success, string Detail);

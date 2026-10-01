namespace BallBank.Api.Features.Notifications;

/// <summary>Somewhere BallBank can tell people things (ADR-0008): Discord and SMS.</summary>
public interface INotificationChannel
{
    /// <summary>One of <see cref="Domain.Notifications.Channels"/>.</summary>
    string Name { get; }

    /// <summary>
    /// Delivers <paramref name="message"/>. Throws <see cref="NotificationDeliveryException"/> when the channel
    /// did not take it.
    /// </summary>
    Task SendAsync(OutgoingNotification message, CancellationToken cancellation);
}

/// <summary>What a channel is asked to deliver.</summary>
/// <param name="LeagueId">The league the notification belongs to.</param>
/// <param name="NotificationId">The notification's dedupe key, which a channel that reports back says which notification it reports on.</param>
/// <param name="Destination">Where it goes: for Discord a webhook URL, for SMS an E.164 number. Either can be a secret.</param>
public sealed record OutgoingNotification(Guid LeagueId, string NotificationId, string Destination, string Text);

/// <summary>The channel did not accept a notification. Never says where it was sent: a destination can be a secret.</summary>
public sealed class NotificationDeliveryException(string message, Exception? inner = null) : Exception(message, inner);

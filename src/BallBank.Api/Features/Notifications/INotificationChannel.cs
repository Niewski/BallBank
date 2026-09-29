namespace BallBank.Api.Features.Notifications;

/// <summary>Somewhere BallBank can tell people things (ADR-0008): Discord today, SMS later.</summary>
public interface INotificationChannel
{
    /// <summary>One of <see cref="Domain.Notifications.Channels"/>.</summary>
    string Name { get; }

    /// <summary>
    /// Delivers <paramref name="text"/> to <paramref name="destination"/> (for Discord, a webhook URL).
    /// Throws <see cref="NotificationDeliveryException"/> when the channel did not take it.
    /// </summary>
    Task SendAsync(string destination, string text, CancellationToken cancellation);
}

/// <summary>The channel did not accept a notification. Never says where it was sent: a destination can be a secret.</summary>
public sealed class NotificationDeliveryException(string message, Exception? inner = null) : Exception(message, inner);

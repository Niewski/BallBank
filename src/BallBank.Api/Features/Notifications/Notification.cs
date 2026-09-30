namespace BallBank.Api.Features.Notifications;

/// <summary>
/// One thing BallBank decided to tell someone. Tenant-scoped like every document, and keyed by its dedupe key
/// (<see cref="Domain.Notifications.NotificationKey"/>): the channel, who is told, what it is about and what caused it.
/// Inserting it claims that key, so an event handled a second time finds it taken and sends nothing (ADR-0008).
/// It commits in the same transaction as the handling of the event, together with the message that sends it.
/// </summary>
public sealed class Notification
{
    public string Id { get; set; } = string.Empty;

    /// <summary>What it is about, one of <see cref="Domain.Notifications.NotificationKinds"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Where it goes, one of <see cref="Domain.Notifications.Channels"/>.</summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>The words, as they were rendered when the event was handled.</summary>
    public string Text { get; set; } = string.Empty;

    public string Status { get; set; } = NotificationStatus.Pending;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the channel accepted it.</summary>
    public DateTimeOffset? SentAt { get; set; }
}

public static class NotificationStatus
{
    /// <summary>Not delivered yet. One that stays pending while its message sits in the dead-letter queue failed for good.</summary>
    public const string Pending = "Pending";

    public const string Sent = "Sent";

    /// <summary>Not sent: the league disconnected the channel after the notification was decided.</summary>
    public const string Dropped = "Dropped";
}

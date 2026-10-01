namespace BallBank.Api.Features.Notifications;

/// <summary>
/// One thing BallBank decided to tell someone. Tenant-scoped like every document, and keyed by its dedupe key
/// (<see cref="Domain.Notifications.NotificationKey"/>): the channel, who is told, what it is about and what caused it.
/// Inserting it claims that key, so an event handled a second time finds it taken and sends nothing (ADR-0008).
/// One decided from an event commits in the same transaction as the handling of the event, together with the message
/// that sends it; a reminder is decided and sent by the tick (ADR-0007).
/// </summary>
public sealed class Notification
{
    public string Id { get; set; } = string.Empty;

    /// <summary>What it is about, one of <see cref="Domain.Notifications.NotificationKinds"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Where it goes, one of <see cref="Domain.Notifications.Channels"/>.</summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>The member it is for, when it is for one; <c>null</c> for the league's own channel.</summary>
    public Guid? MemberId { get; set; }

    /// <summary>The account a reminder is about; <c>null</c> for anything else.</summary>
    public Guid? AccountId { get; set; }

    /// <summary>The words, as they were rendered when it was decided.</summary>
    public string Text { get; set; } = string.Empty;

    public string Status { get; set; } = NotificationStatus.Pending;

    /// <summary>Why it was <see cref="NotificationStatus.Skipped"/> or <see cref="NotificationStatus.Held"/>.</summary>
    public string? Reason { get; set; }

    /// <summary>When a <see cref="NotificationStatus.Held"/> notification may go: when the member's quiet hours end.</summary>
    public DateTimeOffset? SendAfter { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the channel accepted it.</summary>
    public DateTimeOffset? SentAt { get; set; }
}

public static class NotificationStatus
{
    /// <summary>
    /// Not delivered yet. One that stays pending while its message sits in the dead-letter queue failed for good; a
    /// reminder the channel refused has no message, and stays pending until the next tick sends it.
    /// </summary>
    public const string Pending = "Pending";

    public const string Sent = "Sent";

    /// <summary>Not sent: the league disconnected the channel after the notification was decided.</summary>
    public const string Dropped = "Dropped";

    /// <summary>Not sent, and never will be: the member gave no consent at their number, or the number opted out.</summary>
    public const string Skipped = "Skipped";

    /// <summary>Not sent yet because it fell in the member's quiet hours; it goes once <see cref="Notification.SendAfter"/> has passed.</summary>
    public const string Held = "Held";
}

using BallBank.Domain.Notifications;

namespace BallBank.Api.Features.Notifications;

/// <summary>
/// One thing BallBank decided to tell someone. Tenant-scoped like every document, and keyed by its dedupe key
/// (<see cref="Domain.Notifications.NotificationKey"/>): the channel, who is told, what it is about and what caused it.
/// Inserting it claims that key, so an event handled a second time finds it taken and sends nothing (ADR-0008).
/// One decided from an event commits in the same transaction as the handling of the event, together with the message
/// that sends it; a reminder and a weekly digest are decided and sent by the tick (ADR-0007), which purges the ones
/// older than the retention age (<see cref="RetentionOptions"/>), freeing their keys.
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

    /// <summary>Why it was skipped or held, or the error Twilio gave when it did not deliver it.</summary>
    public string? Reason { get; set; }

    /// <summary>When a <see cref="NotificationStatus.Held"/> notification may go: when the member's quiet hours end.</summary>
    public DateTimeOffset? SendAfter { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the channel accepted it.</summary>
    public DateTimeOffset? SentAt { get; set; }

    /// <summary>Takes Twilio's report of a text; a status only moves forward, so a repeated or late one changes nothing. <c>true</c> when it changed.</summary>
    public bool Settle(TextDelivery delivery, string? errorCode)
    {
        if (delivery == TextDelivery.InFlight || Status != NotificationStatus.Sent)
        {
            return false;
        }

        Status = delivery switch
        {
            TextDelivery.Delivered => NotificationStatus.Delivered,
            TextDelivery.Undelivered => NotificationStatus.Undelivered,
            _ => NotificationStatus.Failed,
        };
        Reason = delivery != TextDelivery.Delivered && errorCode is { Length: > 0 and <= 8 } && errorCode.All(char.IsAsciiDigit)
            ? $"Twilio error {errorCode}"
            : null;
        return true;
    }
}

public static class NotificationStatus
{
    /// <summary>
    /// Not delivered yet. One that stays pending while its message sits in the dead-letter queue failed for good; a
    /// reminder or digest the channel refused has no message, and stays pending until the next tick sends it.
    /// </summary>
    public const string Pending = "Pending";

    public const string Sent = "Sent";

    /// <summary>Not sent: the league disconnected the channel after the notification was decided.</summary>
    public const string Dropped = "Dropped";

    /// <summary>Not sent, and never will be: no consent at the number, or it opted out; the member paid up before a reminder went; or a digest's week ended, or the league stopped wanting it.</summary>
    public const string Skipped = "Skipped";

    /// <summary>Not sent yet because it fell in the member's quiet hours; it goes once <see cref="Notification.SendAfter"/> has passed.</summary>
    public const string Held = "Held";

    /// <summary>Twilio reports the text reached the member's phone. Where a sent text ends if all goes well.</summary>
    public const string Delivered = "Delivered";

    /// <summary>Twilio sent the text, and the carrier did not deliver it.</summary>
    public const string Undelivered = "Undelivered";

    /// <summary>Twilio could not send the text.</summary>
    public const string Failed = "Failed";
}

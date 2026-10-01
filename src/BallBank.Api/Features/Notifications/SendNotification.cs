using BallBank.Api.Features.Membership;
using BallBank.Domain.Notifications;
using Marten;

namespace BallBank.Api.Features.Notifications;

/// <summary>Deliver a <see cref="Notification"/> that was decided, on the durable "notifications" queue (ADR-0008).</summary>
/// <param name="LeagueId">Whose notification it is: its channel, or the league its member is in.</param>
/// <param name="NotificationId">The notification's dedupe key.</param>
public sealed record SendNotification(Guid LeagueId, string NotificationId);

public static class SendNotificationHandler
{
    public const string Queue = "notifications";

    /// <summary>
    /// Sends what was decided and marks it sent; a channel that refuses throws, and Wolverine retries and then dead-letters.
    /// A text is checked as it goes: skipped without consent or after STOP, held through quiet hours.
    /// </summary>
    public static async Task Handle(
        SendNotification command,
        IDocumentSession session,
        IDocumentStore store,
        NotificationChannels channels,
        TimeProvider clock,
        CancellationToken cancellation)
    {
        var notification = await session.LoadAsync<Notification>(command.NotificationId, cancellation);
        if (notification is not { Status: NotificationStatus.Pending or NotificationStatus.Held })
        {
            return;
        }

        var now = clock.GetUtcNow();
        var destination = notification.Channel == Channels.Sms
            ? await TextDestinationAsync(notification, session, store, now, cancellation)
            : await DiscordDestinationAsync(notification, command.LeagueId, session, cancellation);

        if (destination is null)
        {
            session.Store(notification);
            return;
        }

        await channels.For(notification.Channel).SendAsync(
            new OutgoingNotification(command.LeagueId, notification.Id, destination, notification.Text), cancellation);

        notification.Status = NotificationStatus.Sent;
        notification.SentAt = now;
        notification.Reason = null;
        notification.SendAfter = null;
        session.Store(notification);
    }

    // The webhook of the league's channel; null, and the notification dropped, when the league disconnected it.
    private static async Task<string?> DiscordDestinationAsync(
        Notification notification, Guid leagueId, IDocumentSession session, CancellationToken cancellation)
    {
        var settings = await session.LoadAsync<LeagueNotificationSettings>(leagueId, cancellation);
        if (settings is null)
        {
            notification.Status = NotificationStatus.Dropped;
            return null;
        }

        return settings.DiscordWebhookUrl;
    }

    // The member's number; null when they are not to be texted now, with the notification skipped or held.
    private static async Task<string?> TextDestinationAsync(
        Notification notification, IDocumentSession session, IDocumentStore store, DateTimeOffset now, CancellationToken cancellation)
    {
        var memberId = notification.MemberId!.Value;
        var phone = (await session.LoadAsync<MemberContact>(memberId, cancellation))?.Phone;
        var preferences = await session.LoadAsync<NotificationPreferences>(memberId, cancellation);
        var optedOut = await PhoneOptOuts.IsOptedOutAsync(store, phone, cancellation);

        var delivery = SmsDelivery.Decide(
            preferences?.ToConsent(), phone, optedOut, preferences?.ToQuietHours() ?? QuietHours.Default, now);

        switch (delivery.Outcome)
        {
            case SmsOutcome.Skip:
                notification.Status = NotificationStatus.Skipped;
                notification.Reason = delivery.Reason;
                notification.SendAfter = null;
                return null;
            case SmsOutcome.Hold:
                notification.Status = NotificationStatus.Held;
                notification.Reason = delivery.Reason;
                notification.SendAfter = delivery.SendAfter;
                return null;
            default:
                return phone;
        }
    }
}

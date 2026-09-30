using BallBank.Domain.Notifications;
using Marten;

namespace BallBank.Api.Features.Notifications;

/// <summary>Deliver a <see cref="Notification"/> that was decided, on the durable "notifications" queue (ADR-0008).</summary>
/// <param name="LeagueId">Whose channel it goes to.</param>
/// <param name="NotificationId">The notification's dedupe key.</param>
public sealed record SendNotification(Guid LeagueId, string NotificationId);

public static class SendNotificationHandler
{
    public const string Queue = "notifications";

    /// <summary>
    /// Sends what was decided to the league's channel and marks it sent. A channel that does not take it throws
    /// <see cref="NotificationDeliveryException"/>, which Wolverine retries on a schedule and then dead-letters;
    /// the notification stays pending, and nothing that caused it is held up. A notification already sent, or
    /// dropped, is not sent again, and a league that disconnected since it was decided is not posted to.
    /// </summary>
    public static async Task Handle(
        SendNotification command,
        IDocumentSession session,
        NotificationChannels channels,
        TimeProvider clock,
        CancellationToken cancellation)
    {
        var notification = await session.LoadAsync<Notification>(command.NotificationId, cancellation);
        if (notification is not { Status: NotificationStatus.Pending })
        {
            return;
        }

        var settings = await session.LoadAsync<LeagueNotificationSettings>(command.LeagueId, cancellation);
        if (settings is null)
        {
            notification.Status = NotificationStatus.Dropped;
            session.Store(notification);
            return;
        }

        await channels.For(notification.Channel).SendAsync(settings.DiscordWebhookUrl, notification.Text, cancellation);

        notification.Status = NotificationStatus.Sent;
        notification.SentAt = clock.GetUtcNow();
        session.Store(notification);
    }
}

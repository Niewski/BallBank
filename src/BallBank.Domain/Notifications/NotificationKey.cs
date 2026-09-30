namespace BallBank.Domain.Notifications;

/// <summary>
/// The dedupe key of a notification: the channel, who is told, and what caused it. It is the
/// notification's id, so telling the same recipient about the same cause on the same channel a second
/// time is a collision, and a retried event notifies once (ADR-0008).
/// </summary>
public static class NotificationKey
{
    /// <param name="kind">What the cause was, one of <see cref="NotificationKinds"/>.</param>
    /// <param name="cause">The id of the fact the notification tells of: a season, an attestation.</param>
    /// <param name="recipient">Who is told: today, a league's own channel, named by the league id.</param>
    public static string For(string kind, Guid cause, string channel, Guid recipient) =>
        $"{channel}/{recipient}/{kind}/{cause}";
}

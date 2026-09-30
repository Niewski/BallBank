namespace BallBank.Api.Features.Notifications;

/// <summary>
/// How one league wants to be told things, written straight by its treasurers, with no event, as
/// <see cref="Membership.MemberContact"/> is (ADR-0012). Tenant-scoped like every document, keyed by league.
/// It exists only while Discord is connected; disconnecting deletes it, webhook and flags together.
/// The webhook URL is a secret: whoever holds it can post to the channel, so no response carries it whole.
/// </summary>
public sealed class LeagueNotificationSettings
{
    public Guid Id { get; set; }

    public string DiscordWebhookUrl { get; set; } = string.Empty;

    /// <summary>Whether a confirmed payment is announced in the channel.</summary>
    public bool AnnouncePayments { get; set; }

    /// <summary>Whether the weekly digest is posted to the channel.</summary>
    public bool PostDigest { get; set; }
}

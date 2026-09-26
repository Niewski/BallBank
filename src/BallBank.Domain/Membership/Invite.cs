namespace BallBank.Domain.Membership;

/// <summary>An invite issued to claim one member, usable until <see cref="ExpiresAt"/> unless a newer one voids it.</summary>
public sealed record Invite(Guid InviteId, Guid MemberId, DateTimeOffset ExpiresAt);

/// <summary>Whether an invite can be used to claim its member at a given moment, and if not, why not.</summary>
public enum InviteStatus
{
    /// <summary>No invite with that id was issued in this league.</summary>
    NotIssued,

    /// <summary>It can be used now.</summary>
    Valid,

    /// <summary>A newer invite for the same member voided it.</summary>
    Replaced,

    /// <summary>Its fourteen days are over.</summary>
    Expired,
}

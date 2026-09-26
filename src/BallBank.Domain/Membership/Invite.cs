namespace BallBank.Domain.Membership;

/// <summary>An invite issued to claim one member, usable until <see cref="ExpiresAt"/> unless a newer one voids it.</summary>
public sealed record Invite(Guid InviteId, Guid MemberId, DateTimeOffset ExpiresAt);

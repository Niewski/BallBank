namespace BallBank.Domain.Membership;

// Events are facts that already happened. Past tense, immutable, never edited once released:
// a change in shape is a new event type, not a modified one (see docs/domain.md).
// Each carries the sign-in subject of the person who caused it and when.

/// <summary>A Sleeper league was brought into BallBank. The raw Sleeper responses are kept under <see cref="SnapshotId"/>.</summary>
public sealed record LeagueImported(
    Guid LeagueId,
    string SleeperLeagueId,
    string Name,
    string Season,
    Guid SnapshotId,
    string ImportedBy,
    DateTimeOffset ImportedAt);

/// <summary>A Sleeper roster became a member of the league: one member per roster, taken from its owner (ADR-0010).</summary>
public sealed record MemberAdded(
    Guid MemberId,
    int SleeperRosterId,
    string? SleeperUserId,
    string TeamName,
    string? SleeperDisplayName,
    bool SuggestedTreasurer,
    string AddedBy,
    DateTimeOffset AddedAt);

/// <summary>An identity claimed a member. <see cref="InviteId"/> is <c>null</c> for the importer's own claim.</summary>
public sealed record MemberClaimed(
    Guid MemberId,
    string Subject,
    string DisplayName,
    Guid? InviteId,
    DateTimeOffset ClaimedAt);

/// <summary>A member was made a treasurer of the league.</summary>
public sealed record TreasurerAppointed(
    Guid MemberId,
    string AppointedBy,
    DateTimeOffset AppointedAt);

/// <summary>
/// A treasurer issued an invite to claim a member, good until <see cref="ExpiresAt"/>. Only the latest
/// invite for a member is valid; it voids every earlier one.
/// </summary>
public sealed record InviteIssued(
    Guid InviteId,
    Guid MemberId,
    string IssuedBy,
    DateTimeOffset ExpiresAt,
    DateTimeOffset IssuedAt);

/// <summary>
/// A treasurer revoked an identity's claim to a member: the wrong person claimed it, or its owner
/// left. The member keeps its account and history for whoever claims it next (ADR-0010).
/// </summary>
public sealed record MemberClaimRevoked(
    Guid MemberId,
    string Subject,
    string RevokedBy,
    string Reason,
    DateTimeOffset RevokedAt);

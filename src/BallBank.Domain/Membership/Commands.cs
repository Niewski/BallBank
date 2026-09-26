namespace BallBank.Domain.Membership;

// Commands are requests. The aggregate decides whether each one becomes events.
// Ids are supplied by the caller so a retried command is recognised rather than repeated.

/// <summary>
/// Bring a Sleeper league into BallBank as the league <see cref="LeagueId"/>.
/// </summary>
/// <param name="MemberIds">The member id to give each roster, keyed by Sleeper roster id.</param>
/// <param name="ImporterSubject">The sign-in subject of the person importing.</param>
/// <param name="ImporterSleeperUserId">The Sleeper user id the importer's username resolved to.</param>
/// <param name="ImporterDisplayName">What the importer wants to be called in this league.</param>
/// <param name="SleeperLeagueAlreadyBacks">The BallBank league this Sleeper league already backs, if any.</param>
public sealed record ImportLeague(
    Guid LeagueId,
    Guid SnapshotId,
    SleeperLeagueSnapshot Snapshot,
    IReadOnlyDictionary<int, Guid> MemberIds,
    string ImporterSubject,
    string ImporterSleeperUserId,
    string ImporterDisplayName,
    Guid? SleeperLeagueAlreadyBacks);

/// <summary>
/// Import a league from its Sleeper league again, to pick up teams that joined since. Decided by the
/// league it is imported into.
/// </summary>
/// <param name="MemberIds">The member id to give each roster not yet a member, keyed by Sleeper roster id.</param>
/// <param name="ImporterSubject">The sign-in subject of the treasurer importing.</param>
public sealed record ImportLeagueAgain(
    SleeperLeagueSnapshot Snapshot,
    IReadOnlyDictionary<int, Guid> MemberIds,
    string ImporterSubject);

/// <summary>
/// Record how to reach a member, replacing whatever was recorded before. Decided by the league, which
/// knows who may change whose, into the details to keep rather than an event: personal data is never
/// written to the event store (ADR-0012). What it creates is keyed by the member, so a replay rewrites
/// the same details and needs no id of its own.
/// </summary>
/// <param name="EditorSubject">The sign-in subject of the person changing them.</param>
/// <param name="Email">As entered; blank clears it.</param>
/// <param name="Phone">As entered, in any common US format; blank clears it.</param>
/// <param name="DiscordUsername">As entered, with or without a leading <c>@</c>; blank clears it.</param>
public sealed record RecordContactDetails(
    Guid MemberId,
    string EditorSubject,
    string? Email,
    string? Phone,
    string? DiscordUsername);

/// <summary>
/// Issue an invite for one member, so whoever opens it can claim exactly that member. The league
/// decides when it expires; issuing another for the same member voids this one.
/// </summary>
/// <param name="IssuerSubject">The sign-in subject of the treasurer issuing it.</param>
public sealed record IssueInvite(
    Guid InviteId,
    Guid MemberId,
    string IssuerSubject);

/// <summary>
/// Claim a member through an invite, so the identity <see cref="ClaimantSubject"/> becomes that member.
/// Replaying it, by the identity that already holds the member, claims nothing more.
/// </summary>
/// <param name="ClaimantSubject">The sign-in subject of the person claiming.</param>
/// <param name="DisplayName">What the claimant wants to be called in this league.</param>
public sealed record ClaimMember(
    Guid MemberId,
    Guid InviteId,
    string ClaimantSubject,
    string DisplayName);

/// <summary>
/// Make a claimed member another treasurer of the league. What it creates is keyed by the member, so
/// replaying it, once the member is a treasurer, appoints nothing more.
/// </summary>
/// <param name="AppointerSubject">The sign-in subject of the treasurer appointing them.</param>
public sealed record AppointTreasurer(
    Guid MemberId,
    string AppointerSubject);

/// <summary>
/// Revoke <see cref="Subject"/>'s claim to a member, with a reason: the wrong person claimed it, or
/// its owner left. The member keeps its account and history for whoever claims it next (ADR-0010).
/// Replaying it, once <see cref="Subject"/> no longer holds the member, revokes nothing more: they
/// may have been revoked already, or the member may have been claimed since by someone else.
/// </summary>
/// <param name="Subject">The sign-in subject whose claim is being revoked.</param>
/// <param name="RevokerSubject">The sign-in subject of the treasurer revoking it.</param>
public sealed record RevokeClaim(
    Guid MemberId,
    string Subject,
    string RevokerSubject,
    string Reason);

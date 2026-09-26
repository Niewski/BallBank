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

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

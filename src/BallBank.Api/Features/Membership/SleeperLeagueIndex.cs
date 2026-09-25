namespace BallBank.Api.Features.Membership;

/// <summary>
/// The BallBank league a Sleeper league backs, keyed by Sleeper league id. One of the two cross-tenant
/// documents (ADR-0011): one Sleeper league backs only one BallBank league, and only a lookup outside
/// any one league can tell. Inserted, never upserted, in the same transaction as <c>LeagueImported</c>,
/// so two imports racing for one Sleeper league cannot both commit.
/// </summary>
public sealed class SleeperLeagueIndex
{
    /// <summary>The Sleeper league id.</summary>
    public string Id { get; set; } = "";

    public Guid LeagueId { get; set; }
}

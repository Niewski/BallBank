using BallBank.Domain.Membership;

namespace BallBank.Api.Integrations.Sleeper;

/// <summary>
/// What Sleeper said about one league, read and raw: the reading is what an import decides from,
/// the raw JSON is what it keeps so a mapping question can be answered later from Sleeper's own words.
/// </summary>
public sealed record SleeperLeagueResponses(
    SleeperLeague League,
    IReadOnlyList<SleeperLeagueUser> Users,
    IReadOnlyList<SleeperRoster> Rosters,
    string LeagueJson,
    string UsersJson,
    string RostersJson)
{
    /// <summary>
    /// The league as the domain sees it. A team's name is its owner's name for it in this league;
    /// co-owners are not read at all, so they never become members (ADR-0010).
    /// </summary>
    public SleeperLeagueSnapshot ToSnapshot()
    {
        var teamNames = Users.DistinctBy(u => u.UserId).ToDictionary(u => u.UserId, u => u.TeamName);

        return new SleeperLeagueSnapshot(
            League.LeagueId,
            League.Name,
            League.Season,
            Users.Select(u => new SleeperLeagueSnapshot.User(u.UserId, u.DisplayName, u.IsCommissioner)).ToList(),
            Rosters
                .Select(r => new SleeperLeagueSnapshot.Roster(
                    r.RosterId,
                    r.OwnerId,
                    r.OwnerId is null ? null : teamNames.GetValueOrDefault(r.OwnerId)))
                .ToList());
    }
}

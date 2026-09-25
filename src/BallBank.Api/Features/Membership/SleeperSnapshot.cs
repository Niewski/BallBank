using BallBank.Api.Integrations.Sleeper;

namespace BallBank.Api.Features.Membership;

/// <summary>
/// The raw Sleeper responses one import decided from, kept in the league's tenant under the id that
/// <c>LeagueImported</c> carries, so a mapping question can be answered from what Sleeper actually said.
/// </summary>
public sealed class SleeperSnapshot
{
    public Guid Id { get; set; }

    public string SleeperLeagueId { get; set; } = "";

    public DateTimeOffset TakenAt { get; set; }

    /// <summary><c>/league/{id}</c>, verbatim.</summary>
    public string League { get; set; } = "";

    /// <summary><c>/league/{id}/users</c>, verbatim.</summary>
    public string Users { get; set; } = "";

    /// <summary><c>/league/{id}/rosters</c>, verbatim.</summary>
    public string Rosters { get; set; } = "";

    public static SleeperSnapshot Of(Guid id, SleeperLeagueResponses responses, DateTimeOffset takenAt) => new()
    {
        Id = id,
        SleeperLeagueId = responses.League.LeagueId,
        TakenAt = takenAt,
        League = responses.LeagueJson,
        Users = responses.UsersJson,
        Rosters = responses.RostersJson,
    };
}

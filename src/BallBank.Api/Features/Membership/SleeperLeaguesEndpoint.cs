using BallBank.Api.Integrations.Sleeper;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace BallBank.Api.Features.Membership;

/// <summary>One league in the import picker: enough to tell it apart before importing it.</summary>
public sealed record SleeperLeagueChoice(string SleeperLeagueId, string Name, string Season, int Teams);

public static class SleeperLeaguesEndpoint
{
    /// <summary>The picker on the import page: a Sleeper user's leagues for the current season.</summary>
    [Authorize]
    [WolverineGet("/sleeper/users/{username}/leagues")]
    public static async Task<IResult> Get(string username, SleeperClient sleeper, CancellationToken cancellation)
    {
        var user = await sleeper.FindUserAsync(username, cancellation);
        if (user is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "No such Sleeper user",
                detail: $"Sleeper has no user called \"{username}\". Check the spelling of your Sleeper username.");
        }

        var season = await sleeper.CurrentSeasonAsync(cancellation);
        var leagues = await sleeper.GetLeaguesAsync(user.UserId, season, cancellation);

        return Results.Ok(leagues
            .Select(l => new SleeperLeagueChoice(l.LeagueId, l.Name, l.Season, l.TotalRosters))
            .ToArray());
    }
}

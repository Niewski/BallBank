using BallBank.Api.Features.Membership;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace BallBank.Api.Features.Treasury;

public static class SeasonsEndpoint
{
    /// <summary>Every season of the league, for any of its members, most recently opened first.</summary>
    [Authorize(Policy = Policies.LeagueMember)]
    [WolverineGet("/leagues/{leagueId}/seasons")]
    public static async Task<SeasonSummary[]> Get(Guid leagueId, IQuerySession session, CancellationToken cancellation)
    {
        var seasons = await session.Query<SeasonListing>().ToListAsync(cancellation);

        return seasons
            .OrderByDescending(s => s.OpenedAt)
            .Select(SeasonSummary.Of)
            .ToArray();
    }
}

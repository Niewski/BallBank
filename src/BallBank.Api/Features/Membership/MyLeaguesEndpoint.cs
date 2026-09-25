using System.Security.Claims;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace BallBank.Api.Features.Membership;

/// <summary>One entry of <c>GET /me/leagues</c>.</summary>
public sealed record MyLeague(Guid LeagueId, string Name, string Season, Guid MemberId, IReadOnlyList<string> Roles);

public static class MyLeaguesEndpoint
{
    [Authorize]
    [WolverineGet("/me/leagues")]
    public static async Task<MyLeague[]> Get(ClaimsPrincipal user, IQuerySession session, CancellationToken cancellation)
    {
        var memberships = await session.LoadAsync<UserMemberships>(user.Subject(), cancellation);

        return memberships?.Leagues
            .Select(l => new MyLeague(l.LeagueId, l.LeagueName, l.Season, l.MemberId, l.Roles))
            .ToArray() ?? [];
    }
}

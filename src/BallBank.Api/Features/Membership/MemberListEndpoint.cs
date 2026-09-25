using System.Security.Claims;
using BallBank.Domain.Membership;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace BallBank.Api.Features.Membership;

/// <summary>What <c>GET /leagues/{leagueId}/members</c> answers: the league and every member of it.</summary>
/// <param name="SleeperLeagueId">The Sleeper league the league is imported from, which importing again names.</param>
/// <param name="YourMemberId">The member the caller holds.</param>
public sealed record LeagueMembers(
    Guid LeagueId,
    string Name,
    string Season,
    string SleeperLeagueId,
    Guid? YourMemberId,
    IReadOnlyList<LeagueMemberEntry> Members);

/// <summary>One member on the member list.</summary>
/// <param name="HolderDisplayName">What the identity holding this member goes by; <c>null</c> while unclaimed.</param>
public sealed record LeagueMemberEntry(
    Guid MemberId,
    string TeamName,
    string? SleeperDisplayName,
    bool Claimed,
    string? HolderDisplayName,
    bool SuggestedTreasurer,
    IReadOnlyList<string> Roles);

public static class MemberListEndpoint
{
    /// <summary>Every member of the league, for any of its members, in team name order.</summary>
    [Authorize(Policy = Policies.LeagueMember)]
    [WolverineGet("/leagues/{leagueId}/members")]
    public static async Task<IResult> Get(Guid leagueId, ClaimsPrincipal user, IQuerySession session, CancellationToken cancellation)
    {
        var league = await session.Events.AggregateStreamAsync<League>(leagueId, token: cancellation);
        if (league is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new LeagueMembers(
            league.Id,
            league.Name,
            league.Season,
            league.SleeperLeagueId,
            league.MemberHeldBy(user.Subject())?.MemberId,
            league.Members
                .OrderBy(m => m.TeamName, StringComparer.OrdinalIgnoreCase)
                .Select(m => new LeagueMemberEntry(
                    m.MemberId,
                    m.TeamName,
                    m.SleeperDisplayName,
                    m.IsClaimed,
                    m.HolderDisplayName,
                    m.SuggestedTreasurer,
                    Roles.Of(m)))
                .ToArray()));
    }
}

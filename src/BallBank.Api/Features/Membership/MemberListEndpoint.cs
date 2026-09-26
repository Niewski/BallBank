using System.Security.Claims;
using BallBank.Domain.Membership;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace BallBank.Api.Features.Membership;

/// <summary>What <c>GET /leagues/{leagueId}/members</c> answers: the league and every member of it.</summary>
/// <param name="YourMemberId">The member the caller holds.</param>
public sealed record LeagueMembers(
    Guid LeagueId,
    string Name,
    string Season,
    Guid? YourMemberId,
    IReadOnlyList<LeagueMemberEntry> Members);

/// <summary>One member on the member list.</summary>
/// <param name="HolderDisplayName">What the identity holding this member goes by; <c>null</c> while unclaimed.</param>
/// <param name="HeldBySubject">
/// The sign-in subject of the identity holding this member, so a treasurer can revoke its claim;
/// <c>null</c> while unclaimed, or for anyone but a treasurer.
/// </param>
/// <param name="Contact">
/// How to reach the member, for a treasurer and for the member themselves, who may also change it;
/// <c>null</c> for anyone else. Fields with nothing recorded are <c>null</c>.
/// </param>
public sealed record LeagueMemberEntry(
    Guid MemberId,
    string TeamName,
    string? SleeperDisplayName,
    bool Claimed,
    string? HolderDisplayName,
    string? HeldBySubject,
    bool SuggestedTreasurer,
    IReadOnlyList<string> Roles,
    ContactDetails? Contact);

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

        var caller = league.MemberHeldBy(user.Subject());
        var contacts = await ContactsVisibleTo(caller, session, cancellation);
        var isTreasurer = caller is { IsTreasurer: true };

        return Results.Ok(new LeagueMembers(
            league.Id,
            league.Name,
            league.Season,
            caller?.MemberId,
            league.Members
                .OrderBy(m => m.TeamName, StringComparer.OrdinalIgnoreCase)
                .Select(m => new LeagueMemberEntry(
                    m.MemberId,
                    m.TeamName,
                    m.SleeperDisplayName,
                    m.IsClaimed,
                    m.HolderDisplayName,
                    isTreasurer ? m.HeldBy : null,
                    m.SuggestedTreasurer,
                    Roles.Of(m),
                    contacts(m.MemberId)))
                .ToArray()));
    }

    /// <summary>
    /// Every member's contact details for a treasurer, a member's own for them, nobody's for anyone else:
    /// what is not shown is not read.
    /// </summary>
    private static async Task<Func<Guid, ContactDetails?>> ContactsVisibleTo(Member? caller, IQuerySession session, CancellationToken cancellation)
    {
        if (caller is { IsTreasurer: true })
        {
            var all = (await session.Query<MemberContact>().ToListAsync(cancellation)).ToDictionary(c => c.Id, c => c.Details);
            return memberId => all.GetValueOrDefault(memberId, ContactDetails.None);
        }

        if (caller is not null)
        {
            var own = (await session.LoadAsync<MemberContact>(caller.MemberId, cancellation))?.Details ?? ContactDetails.None;
            return memberId => memberId == caller.MemberId ? own : null;
        }

        return _ => null;
    }
}

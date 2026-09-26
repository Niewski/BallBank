using System.Security.Claims;
using BallBank.Domain.Membership;
using JasperFx;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace BallBank.Api.Features.Membership;

/// <summary>The body of <c>POST /leagues/{leagueId}/treasurers</c>: the member to appoint.</summary>
public sealed record AppointTreasurerRequest(Guid MemberId);

public static class TreasurerEndpoint
{
    /// <summary>
    /// Makes a claimed member another treasurer of the league. There is one Treasurer role, so
    /// appointing someone who already holds it changes nothing (ADR-0010). The appointment and the
    /// appointee's membership commit in one transaction.
    /// </summary>
    [Authorize(Policy = Policies.LeagueTreasurer)]
    [WolverinePost("/leagues/{leagueId}/treasurers")]
    public static async Task<IResult> Post(
        Guid leagueId,
        AppointTreasurerRequest request,
        ClaimsPrincipal user,
        IDocumentStore store,
        CancellationToken cancellation)
    {
        // Committed explicitly below, so a league changed since it was read is a 409 rather than a 500.
        await using var session = store.LightweightSession(leagueId.ToString());

        var stream = await session.Events.FetchForWriting<League>(leagueId, cancellation);
        var league = stream.Aggregate;
        if (league?.Members.SingleOrDefault(m => m.MemberId == request.MemberId) is not { } member)
        {
            return Results.NotFound();
        }

        // The policy asked the caller's memberships; the domain refuses this too, from the league
        // itself. Asking the league here first keeps that refusal a 403 should the two ever disagree.
        if (league.MemberHeldBy(user.Subject()) is not { IsTreasurer: true })
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Not a treasurer",
                detail: $"Only a treasurer of {league.Name} can appoint another treasurer.");
        }

        // An unclaimed member is a DomainException: a 409 carrying its message.
        var appointed = league.AppointTreasurer(new AppointTreasurer(member.MemberId, user.Subject()), DateTimeOffset.UtcNow);
        if (appointed is null)
        {
            return Results.NoContent();
        }

        stream.AppendOne(appointed);
        league.Evolve(appointed);

        // UserMemberships lives in the default tenant (ADR-0011), written in the same transaction as the appointment.
        var appointee = league.Members.Single(m => m.MemberId == member.MemberId);
        var memberships = await session.LoadAsync<UserMemberships>(appointee.HeldBy!, cancellation)
            ?? throw new InvalidOperationException("A claimed member's identity has no memberships.");
        memberships.GiveRoles(leagueId, Roles.Of(appointee));
        session.Store(memberships);

        try
        {
            await session.SaveChangesAsync(cancellation);
        }
        catch (ConcurrencyException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "League changed",
                detail: $"{league.Name} changed while the treasurer was being appointed. Look at the league, then appoint again.");
        }

        return Results.NoContent();
    }
}

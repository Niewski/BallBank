using System.Security.Claims;
using BallBank.Domain.Membership;
using JasperFx;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace BallBank.Api.Features.Membership;

/// <summary>The body of <c>DELETE /leagues/{leagueId}/members/{memberId}/claims/{subject}</c>: the reason.</summary>
public sealed record RevokeClaimRequest(string? Reason = null);

public static class RevokeClaimEndpoint
{
    /// <summary>
    /// Revokes an identity's claim to a member, with a reason: the wrong person claimed it, or its
    /// owner left. The member keeps its account and history for whoever claims it next (ADR-0010).
    /// The revocation and the loss of the revoked identity's membership commit in one transaction.
    /// </summary>
    [Authorize(Policy = Policies.LeagueTreasurer)]
    [WolverineDelete("/leagues/{leagueId}/members/{memberId}/claims/{subject}")]
    public static async Task<IResult> Delete(
        Guid leagueId,
        Guid memberId,
        string subject,
        RevokeClaimRequest request,
        ClaimsPrincipal user,
        IDocumentStore store,
        CancellationToken cancellation)
    {
        // Committed explicitly below, so a league changed since it was read is a 409 rather than a 500.
        await using var session = store.LightweightSession(leagueId.ToString());

        var stream = await session.Events.FetchForWriting<League>(leagueId, cancellation);
        var league = stream.Aggregate;
        if (league?.Members.SingleOrDefault(m => m.MemberId == memberId) is not { } member)
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
                detail: $"Only a treasurer of {league.Name} can revoke a claim.");
        }

        // What was entered is checked before the league, as bad input: a 400.
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Revoke not accepted",
                detail: "Revoking a claim needs a reason.");
        }

        // The league decides whether this subject still holds the member: already revoked, never
        // claimed, or claimed since by someone else are all a null no-op, a 204.
        // Refusing to revoke the only treasurer's claim is a DomainException: a 409 carrying its message.
        var revoked = league.RevokeClaim(new RevokeClaim(memberId, subject, user.Subject(), request.Reason), DateTimeOffset.UtcNow);
        if (revoked is null)
        {
            return Results.NoContent();
        }

        stream.AppendOne(revoked);
        league.Evolve(revoked);

        // UserMemberships lives in the default tenant (ADR-0011), written in the same transaction as the revocation.
        var memberships = await session.LoadAsync<UserMemberships>(subject, cancellation)
            ?? throw new InvalidOperationException("A claimed member's identity has no memberships.");
        memberships.RemoveLeague(leagueId);
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
                detail: $"{league.Name} changed while the claim was being revoked. Look at the league, then revoke again.");
        }

        return Results.NoContent();
    }
}

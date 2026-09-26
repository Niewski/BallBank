using System.Security.Claims;
using BallBank.Domain.Membership;
using JasperFx;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace BallBank.Api.Features.Membership;

/// <summary>The body of <c>POST /leagues/{leagueId}/members/{memberId}/invites</c>.</summary>
/// <param name="InviteId">The id to give the invite, chosen by the client so a retry issues nothing new.</param>
public sealed record IssueInviteRequest(Guid InviteId);

/// <summary>An invite a treasurer issued, with the link that claims the member.</summary>
/// <param name="Url">The web app's claim page for this invite, relative to wherever the web app is served.</param>
public sealed record IssuedInvite(Guid InviteId, Guid MemberId, string Url, DateTimeOffset ExpiresAt);

public static class InviteEndpoint
{
    /// <summary>
    /// Issues an invite to claim a member who is not claimed yet, voiding any earlier one for them.
    /// BallBank sends it nowhere: the treasurer copies the link or texts it from their own phone.
    /// </summary>
    [Authorize(Policy = Policies.LeagueTreasurer)]
    [WolverinePost("/leagues/{leagueId}/members/{memberId}/invites")]
    public static async Task<IResult> Post(
        Guid leagueId,
        Guid memberId,
        IssueInviteRequest request,
        ClaimsPrincipal user,
        IDocumentStore store,
        CancellationToken cancellation)
    {
        // Committed explicitly below, so a league changed since it was read is a 409 rather than a 500.
        await using var session = store.LightweightSession(leagueId.ToString());

        var stream = await session.Events.FetchForWriting<League>(leagueId, cancellation);
        var league = stream.Aggregate;
        if (league is null || league.Members.All(m => m.MemberId != memberId))
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
                detail: $"Only a treasurer of {league.Name} can invite someone to claim a member.");
        }

        if (request.InviteId == Guid.Empty)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "No invite id",
                detail: "Issuing an invite needs an id for it, so a retry is recognised rather than issuing another.");
        }

        // A claimed member is a DomainException: a 409 carrying its message.
        var issued = league.IssueInvite(new IssueInvite(request.InviteId, memberId, user.Subject()), DateTimeOffset.UtcNow);
        if (issued is not null)
        {
            stream.AppendOne(issued);

            try
            {
                await session.SaveChangesAsync(cancellation);
            }
            catch (ConcurrencyException)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "League changed",
                    detail: $"{league.Name} changed while the invite was being issued. Look at the league, then invite again.");
            }

            league.Evolve(issued);
        }

        // A retry answers with the invite it already issued.
        var invite = league.InviteWithId(request.InviteId)
            ?? throw new InvalidOperationException("An invite was issued but the league does not have it.");

        return Results.Created(
            (string?)null,
            new IssuedInvite(invite.InviteId, invite.MemberId, $"/claim?league={leagueId}&invite={invite.InviteId}", invite.ExpiresAt));
    }
}

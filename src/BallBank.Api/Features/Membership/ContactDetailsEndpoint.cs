using System.Security.Claims;
using BallBank.Domain;
using BallBank.Domain.Membership;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace BallBank.Api.Features.Membership;

/// <summary>
/// The body of <c>PUT /leagues/{leagueId}/members/{memberId}/contact</c>: every field as entered.
/// It replaces what was recorded, so a field left out or blank is cleared.
/// </summary>
/// <param name="Phone">A US number, written any common way.</param>
/// <param name="DiscordUsername">With or without a leading <c>@</c>.</param>
public sealed record ContactDetailsRequest(string? Email = null, string? Phone = null, string? DiscordUsername = null);

public static class ContactDetailsEndpoint
{
    /// <summary>
    /// Records how to reach a member, for a treasurer or for the member themselves. Written straight to
    /// <see cref="MemberContact"/>, with no event (ADR-0012); clearing every field deletes it.
    /// </summary>
    [Authorize(Policy = Policies.LeagueTreasurerOrOwnMember)]
    [WolverinePut("/leagues/{leagueId}/members/{memberId}/contact")]
    public static async Task<IResult> Put(
        Guid leagueId,
        Guid memberId,
        ContactDetailsRequest request,
        ClaimsPrincipal user,
        IDocumentSession session,
        CancellationToken cancellation)
    {
        var league = await session.Events.AggregateStreamAsync<League>(leagueId, token: cancellation);
        if (league is null || league.Members.All(m => m.MemberId != memberId))
        {
            return Results.NotFound();
        }

        // The policy asked the caller's memberships; the domain refuses this too, from the league
        // itself. Asking the league here first keeps that refusal a 403 should the two ever disagree.
        if (league.MemberHeldBy(user.Subject()) is not { } editor || (!editor.IsTreasurer && editor.MemberId != memberId))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Not yours to change",
                detail: $"Only a treasurer of {league.Name}, or whoever holds that member, can change its contact details.");
        }

        ContactDetails details;
        try
        {
            details = league.RecordContactDetails(
                new RecordContactDetails(memberId, user.Subject(), request.Email, request.Phone, request.DiscordUsername));
        }
        catch (DomainException refusal)
        {
            // Who may change whose is settled above, so what is refused here is what was entered: bad input,
            // a 400, as an incomplete import is, rather than the 409 of a refused change to the books.
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Contact details not accepted",
                detail: refusal.Message);
        }

        if (details == ContactDetails.None)
        {
            session.Delete<MemberContact>(memberId);
        }
        else
        {
            session.Store(MemberContact.Of(memberId, details));
        }

        return Results.NoContent();
    }
}

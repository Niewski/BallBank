using System.Security.Claims;
using BallBank.Domain;
using BallBank.Domain.Membership;
using JasperFx;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace BallBank.Api.Features.Membership;

/// <summary>
/// The body of <c>POST /leagues/{leagueId}/claims</c>: the invite, the name to go by, and how to reach
/// the claimant, as entered.
/// </summary>
/// <param name="InviteId">The invite being used; it names the member.</param>
/// <param name="DisplayName">What the claimant wants to be called in this league.</param>
/// <param name="Email">Required: a claimed member can always be reached.</param>
/// <param name="Phone">A US number, written any common way.</param>
/// <param name="DiscordUsername">With or without a leading <c>@</c>.</param>
public sealed record ClaimMemberRequest(
    Guid InviteId,
    string? DisplayName = null,
    string? Email = null,
    string? Phone = null,
    string? DiscordUsername = null);

/// <summary>The member a claim made the caller, as "my leagues" and the member list will show it.</summary>
/// <param name="Name">The league's name.</param>
/// <param name="DisplayName">What the caller goes by in this league.</param>
public sealed record ClaimedMember(
    Guid LeagueId,
    string Name,
    string Season,
    Guid MemberId,
    string TeamName,
    string DisplayName,
    IReadOnlyList<string> Roles);

/// <summary>What <c>GET /leagues/{leagueId}/invites/{inviteId}</c> answers: what the invite would make the caller.</summary>
/// <param name="Name">The league's name.</param>
/// <param name="Yours">The caller already holds this member, perhaps through this very invite.</param>
/// <param name="Refusal">Why the caller cannot claim with this invite, written for them; <c>null</c> when they can.</param>
/// <param name="Contact">
/// What the treasurer recorded for the member, to start the claim form from; <c>null</c> unless the
/// caller can claim with this invite.
/// </param>
public sealed record InviteToClaim(
    Guid LeagueId,
    string Name,
    string Season,
    Guid MemberId,
    string TeamName,
    string? SleeperDisplayName,
    bool Yours,
    string? Refusal,
    ContactDetails? Contact);

public static class ClaimEndpoint
{
    /// <summary>
    /// The league and team an invite is for, so whoever opened it can tell whether the right link
    /// reached them before claiming. Any signed-in person holding the link may look.
    /// </summary>
    [Authorize]
    [WolverineGet("/leagues/{leagueId}/invites/{inviteId}")]
    public static async Task<IResult> Get(
        Guid leagueId,
        Guid inviteId,
        ClaimsPrincipal user,
        IQuerySession session,
        CancellationToken cancellation)
    {
        var league = await session.Events.AggregateStreamAsync<League>(leagueId, token: cancellation);
        if (league?.InviteWithId(inviteId) is not { } invite)
        {
            return NoSuchInvite();
        }

        var member = league.Members.Single(m => m.MemberId == invite.MemberId);
        var subject = user.Subject();

        // The league decides now what it would decide on a claim, with the name the form starts from.
        string? refusal = null;
        try
        {
            league.ClaimMember(
                new ClaimMember(member.MemberId, invite.InviteId, subject, member.SleeperDisplayName ?? member.TeamName),
                DateTimeOffset.UtcNow);
        }
        catch (DomainException refused)
        {
            refusal = refused.Message;
        }

        var yours = member.HeldBy == subject;

        // Contact details are shown only to someone about to become the member they belong to.
        var contact = yours || refusal is not null
            ? null
            : (await session.LoadAsync<MemberContact>(member.MemberId, cancellation))?.Details ?? ContactDetails.None;

        return Results.Ok(new InviteToClaim(
            leagueId,
            league.Name,
            league.Season,
            member.MemberId,
            member.TeamName,
            member.SleeperDisplayName,
            yours,
            refusal,
            contact));
    }

    /// <summary>
    /// Makes the caller the member an invite was issued for. The caller is not a member of the league
    /// yet, so any signed-in person may ask; the invite is what lets them in. The claim, the claimant's
    /// contact details and their membership commit in one transaction.
    /// </summary>
    [Authorize]
    [WolverinePost("/leagues/{leagueId}/claims")]
    public static async Task<IResult> Post(
        Guid leagueId,
        ClaimMemberRequest request,
        ClaimsPrincipal user,
        IDocumentStore store,
        CancellationToken cancellation)
    {
        // Committed explicitly below, so a league changed since it was read is a 409 rather than a 500.
        await using var session = store.LightweightSession(leagueId.ToString());

        var stream = await session.Events.FetchForWriting<League>(leagueId, cancellation);
        var league = stream.Aggregate;
        if (league?.InviteWithId(request.InviteId) is not { } invite)
        {
            return NoSuchInvite();
        }

        // What was entered is checked before the invite, as bad input: a 400.
        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return NotAccepted("Claiming a member needs the name you want to be called in the league.");
        }

        ContactDetails details;
        try
        {
            details = ContactDetails.From(request.Email, request.Phone, request.DiscordUsername);
        }
        catch (DomainException refusal)
        {
            return NotAccepted(refusal.Message);
        }

        if (details.Email is null)
        {
            return NotAccepted("Claiming a member needs an email, so the treasurer can always reach you.");
        }

        var subject = user.Subject();
        var now = DateTimeOffset.UtcNow;

        MemberClaimed? claimed;
        try
        {
            claimed = league.ClaimMember(new ClaimMember(invite.MemberId, invite.InviteId, subject, request.DisplayName), now);
        }
        catch (DomainException refusal)
        {
            // The league refuses a dead invite before anything else, so a refusal while the invite is
            // valid is about who holds what.
            return league.StatusOfInvite(invite.InviteId, now) == InviteStatus.Valid
                ? Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Cannot claim", detail: refusal.Message)
                : Results.Problem(statusCode: StatusCodes.Status410Gone, title: "Invite no longer valid", detail: refusal.Message);
        }

        // A retry by whoever already holds the member finds everything committed the first time.
        if (claimed is not null)
        {
            stream.AppendOne(claimed);
            session.Store(MemberContact.Of(invite.MemberId, details));

            // UserMemberships lives in the default tenant (ADR-0011), written in the same transaction as the claim.
            var memberships = await session.LoadAsync<UserMemberships>(subject, cancellation)
                ?? new UserMemberships { Id = subject };
            var unclaimed = league.Members.Single(m => m.MemberId == invite.MemberId);
            memberships.Leagues.Add(new LeagueMembership(leagueId, league.Name, league.Season, unclaimed.MemberId, Roles.Of(unclaimed)));
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
                    detail: $"{league.Name} changed while you were claiming. Open the invite again.");
            }

            league.Evolve(claimed);
        }

        var member = league.Members.Single(m => m.MemberId == invite.MemberId);
        return Results.Ok(new ClaimedMember(
            leagueId,
            league.Name,
            league.Season,
            member.MemberId,
            member.TeamName,
            member.HolderDisplayName!,
            Roles.Of(member)));
    }

    private static IResult NoSuchInvite() =>
        Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "No such invite",
            detail: "There is no such invite. Check the link, or ask the treasurer for a new one.");

    private static IResult NotAccepted(string detail) =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Claim not accepted", detail: detail);
}

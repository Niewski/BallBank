using System.Security.Claims;
using BallBank.Api.Features.Membership;
using BallBank.Domain;
using BallBank.Domain.Membership;
using BallBank.Domain.Notifications;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace BallBank.Api.Features.Notifications;

/// <summary>
/// The body of <c>PUT /leagues/{leagueId}/members/{memberId}/notifications</c>: everything the member has
/// said about being texted. It replaces what was recorded, so leaving <paramref name="TextMe"/> off withdraws
/// consent, and leaving <paramref name="QuietHours"/> out returns to 9pm to 9am Eastern.
/// </summary>
/// <param name="TextMe">Consent to be texted at the phone number on record.</param>
public sealed record NotificationPreferencesRequest(bool TextMe, QuietHours? QuietHours = null);

/// <summary>What <c>GET /leagues/{leagueId}/members/{memberId}/notifications</c> answers.</summary>
/// <param name="Consent">When, and for which number, the member consented; <c>null</c> if they have not.</param>
/// <param name="OptedOut">The number on record has replied STOP, which overrides consent.</param>
/// <param name="OptedIn">Consent stands for the number on record and nothing overrides it: the member may be texted.</param>
public sealed record NotificationPreferencesReading(SmsConsent? Consent, QuietHours QuietHours, bool OptedOut, bool OptedIn);

public static class NotificationPreferencesEndpoint
{
    /// <summary>
    /// Records what a member wants about being texted, for the member alone: a treasurer cannot opt anyone
    /// in. Written straight to <see cref="NotificationPreferences"/>, with no event (ADR-0012).
    /// </summary>
    [Authorize(Policy = Policies.LeagueMember)]
    [WolverinePut("/leagues/{leagueId}/members/{memberId}/notifications")]
    public static async Task<IResult> Put(
        Guid leagueId,
        Guid memberId,
        NotificationPreferencesRequest request,
        ClaimsPrincipal user,
        IDocumentSession session,
        TimeProvider clock,
        CancellationToken cancellation)
    {
        var league = await session.Events.AggregateStreamAsync<League>(leagueId, token: cancellation);
        if (league is null || league.Members.All(m => m.MemberId != memberId))
        {
            return Results.NotFound();
        }

        if (league.MemberHeldBy(user.Subject())?.MemberId != memberId)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Not yours to change",
                detail: "Only the member themselves can choose whether to be texted, and when not to be.");
        }

        QuietHours quietHours;
        try
        {
            quietHours = request.QuietHours is { } entered
                ? QuietHours.Of(entered.StartHour, entered.EndHour, entered.TimeZone)
                : QuietHours.Default;
        }
        catch (DomainException refusal)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Quiet hours not accepted",
                detail: refusal.Message);
        }

        SmsConsent? consent = null;
        if (request.TextMe)
        {
            var existing = await session.LoadAsync<NotificationPreferences>(memberId, cancellation);
            var phone = (await session.LoadAsync<MemberContact>(memberId, cancellation))?.Phone;

            // No number on record is refused as a DomainException: a 409, the change to consent that cannot be made.
            consent = SmsConsent.Give(phone, existing?.ToConsent(), clock.GetUtcNow());
        }

        session.Store(NotificationPreferences.Of(memberId, consent, quietHours));
        return Results.NoContent();
    }

    /// <summary>What a member has said about being texted, for a treasurer or for the member themselves.</summary>
    [Authorize(Policy = Policies.LeagueTreasurerOrOwnMember)]
    [WolverineGet("/leagues/{leagueId}/members/{memberId}/notifications")]
    public static async Task<IResult> Get(
        Guid leagueId,
        Guid memberId,
        IQuerySession session,
        IDocumentStore store,
        CancellationToken cancellation)
    {
        var league = await session.Events.AggregateStreamAsync<League>(leagueId, token: cancellation);
        if (league is null || league.Members.All(m => m.MemberId != memberId))
        {
            return Results.NotFound();
        }

        var preferences = await session.LoadAsync<NotificationPreferences>(memberId, cancellation);
        var phone = (await session.LoadAsync<MemberContact>(memberId, cancellation))?.Phone;
        var optedOut = await PhoneOptOuts.IsOptedOutAsync(store, phone, cancellation);
        var consent = preferences?.ToConsent();

        return Results.Ok(new NotificationPreferencesReading(
            consent,
            preferences?.ToQuietHours() ?? QuietHours.Default,
            optedOut,
            SmsConsent.PermitsTexting(consent, phone, optedOut)));
    }
}

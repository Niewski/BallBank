using System.Security.Claims;
using BallBank.Api.Features.Membership;
using BallBank.Domain.Membership;
using BallBank.Domain.Treasury;
using JasperFx;
using JasperFx.Events;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace BallBank.Api.Features.Treasury;

/// <summary>The body of <c>POST /leagues/{leagueId}/seasons</c>.</summary>
public sealed record OpenSeasonRequest(string Label, decimal DuesAmount, DateOnly? DueDate);

public static class OpenSeasonEndpoint
{
    /// <summary>
    /// Opens a season with a label, dues amount and due date. In one transaction, every current member
    /// of the league gets an account with the season dues assessed. The season id is deterministic
    /// (league + label), so opening a season that is already open changes nothing and answers with the
    /// same summary a second time.
    /// </summary>
    [Authorize(Policy = Policies.LeagueTreasurer)]
    [WolverinePost("/leagues/{leagueId}/seasons")]
    public static async Task<IResult> Post(
        Guid leagueId,
        OpenSeasonRequest request,
        ClaimsPrincipal user,
        IDocumentStore store,
        CancellationToken cancellation)
    {
        // Committed explicitly below, so a season opened since this was read collides rather than duplicates.
        await using var session = store.LightweightSession(leagueId.ToString());

        var league = await session.Events.AggregateStreamAsync<League>(leagueId, token: cancellation);
        if (league is null)
        {
            return Results.NotFound();
        }

        // The policy asked the caller's memberships; the league, the source of truth, is asked again
        // (ADR-0011), which also gives the acting member id every event below carries.
        if (league.MemberHeldBy(user.Subject()) is not { IsTreasurer: true } treasurer)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Not a treasurer",
                detail: $"Only a treasurer of {league.Name} can open a season.");
        }

        var label = request.Label ?? string.Empty;
        var seasonId = SeasonIds.SeasonId(leagueId, label);
        var stream = await session.Events.FetchForWriting<Season>(seasonId, cancellation);

        var now = DateTimeOffset.UtcNow;
        var opened = Season.Open(
            new OpenSeason(seasonId, leagueId, label, request.DuesAmount, request.DueDate, treasurer.MemberId),
            stream.Aggregate,
            now);

        // Already open: a retry, or a second treasurer's click, adds nothing.
        if (opened is null)
        {
            return Results.Ok(SeasonSummary.Of(stream.Aggregate!));
        }

        var assessmentId = SeasonIds.DuesAssessmentId(seasonId);
        var accounts = league.Members
            .Select(member =>
            {
                var accountId = SeasonIds.AccountId(seasonId, member.MemberId);
                var accountOpened = MemberAccount.Open(new OpenAccount(accountId, leagueId, opened.Label, member.MemberId), now);
                var account = MemberAccount.Replay(accountOpened);
                var assessed = account.Assess(
                    new AssessDues(accountId, assessmentId, opened.DuesAmount, opened.DueDate, "Season dues", treasurer.MemberId),
                    now)!;
                return (AccountId: accountId, Opened: accountOpened, Assessed: assessed);
            })
            .ToList();

        SetSubjectHeader(session.Events.StartStream<Season>(seasonId, [opened]), user.Subject());
        foreach (var account in accounts)
        {
            SetSubjectHeader(
                session.Events.StartStream<MemberAccount>(account.AccountId, [account.Opened, account.Assessed]),
                user.Subject());
        }

        try
        {
            await session.SaveChangesAsync(cancellation);
        }
        catch (Marten.Exceptions.ExistingStreamIdCollisionException)
        {
            // Another treasurer's click opened this season, or one of its accounts, between the checks above and now.
            return Results.Ok(SeasonSummary.Of(Season.Replay(opened)));
        }

        return Results.Created((string?)null, SeasonSummary.Of(Season.Replay(opened)));
    }

    // The caller's sign-in subject, alongside the acting member id already on each event's body.
    private static void SetSubjectHeader(StreamAction stream, string subject)
    {
        foreach (var @event in stream.Events)
        {
            @event.SetHeader(EventHeaders.Subject, subject);
        }
    }
}

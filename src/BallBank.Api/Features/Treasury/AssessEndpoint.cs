using System.Security.Claims;
using BallBank.Api.Features.Membership;
using BallBank.Domain.Membership;
using BallBank.Domain.Treasury;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace BallBank.Api.Features.Treasury;

/// <summary>The body of <c>POST /leagues/{leagueId}/seasons/{season}/assessments</c>.</summary>
/// <param name="MemberIds">The members to assess; every member of the league when omitted.</param>
public sealed record AssessmentRequest(Guid AssessmentId, decimal Amount, DateOnly? DueDate, string? Memo, Guid[]? MemberIds);

/// <summary>An account an assessment was added to.</summary>
/// <param name="Opened">The member had no account this season, and this assessment opened it.</param>
public sealed record AssessedAccount(Guid AccountId, Guid MemberId, bool Opened);

public static class AssessEndpoint
{
    /// <summary>
    /// Assesses an amount (a late fee, a side-pot buy-in) to every member of the league, or to the
    /// members named. In one transaction each targeted account gets a <see cref="DuesAssessed"/> with
    /// the same assessment id, and a member with no account this season gets one opened first. An
    /// account that already carries the assessment is skipped, so repeating it adds nothing. Answers
    /// with the accounts touched. No expected version: the assessment spans many accounts.
    /// </summary>
    [Authorize(Policy = Policies.LeagueTreasurer)]
    [WolverinePost("/leagues/{leagueId}/seasons/{season}/assessments")]
    public static async Task<IResult> Post(
        Guid leagueId,
        string season,
        AssessmentRequest request,
        IdempotentRequest idempotency,
        ClaimsPrincipal user,
        IDocumentStore store,
        CancellationToken cancellation)
    {
        await using var session = store.LightweightSession(leagueId.ToString());

        var league = await session.Events.AggregateStreamAsync<League>(leagueId, token: cancellation);
        var seasonId = SeasonIds.SeasonId(leagueId, season);
        var opened = await session.Events.AggregateStreamAsync<Season>(seasonId, token: cancellation);
        if (league is null || opened is null)
        {
            return Results.NotFound();
        }

        // Asked of the league as well as the policy (ADR-0011); also the acting member every event carries.
        if (league.MemberHeldBy(user.Subject()) is not { IsTreasurer: true } treasurer)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Not a treasurer",
                detail: $"Only a treasurer of {league.Name} can assess.");
        }

        if (Targets(request, league) is not { } memberIds)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Refused",
                detail: request.MemberIds is { Length: 0 }
                    ? "An assessment needs at least one member; name none to assess everyone."
                    : $"Only members of {league.Name} can be assessed.");
        }

        session.SetHeader(EventHeaders.Subject, user.Subject());

        var now = DateTimeOffset.UtcNow;
        var touched = new List<AssessedAccount>();
        foreach (var memberId in memberIds)
        {
            var accountId = SeasonIds.AccountId(seasonId, memberId);
            var command = new AssessDues(accountId, request.AssessmentId, request.Amount, request.DueDate, request.Memo ?? string.Empty, treasurer.MemberId);

            // Checked on commit: an account changed or opened since this read collides rather than duplicates.
            var stream = await session.Events.FetchForWriting<MemberAccount>(accountId, cancellation);
            if (stream.Aggregate is { } account)
            {
                if (account.Assess(command, now) is { } assessed)
                {
                    stream.AppendOne(assessed);
                    touched.Add(new AssessedAccount(accountId, memberId, Opened: false));
                }

                continue;
            }

            // A member added since the season opened: their first assessment opens their account.
            var accountOpened = MemberAccount.Open(new OpenAccount(accountId, leagueId, opened.Label, memberId), now);
            stream.AppendMany(accountOpened, MemberAccount.Replay(accountOpened).Assess(command, now)!);
            touched.Add(new AssessedAccount(accountId, memberId, Opened: true));
        }

        var answer = touched.ToArray();
        return await idempotency.CommitAsync(
            session,
            StatusCodes.Status200OK,
            answer,
            onCollision: () => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Accounts changed",
                detail: "An account changed while it was being assessed. Assess again: members already assessed will not be assessed twice."),
            cancellation);
    }

    // Every member when none are named; null when the list is empty or names someone outside the league.
    private static IReadOnlyList<Guid>? Targets(AssessmentRequest request, League league)
    {
        var members = league.Members.Select(m => m.MemberId).ToHashSet();
        if (request.MemberIds is null)
        {
            return [.. members];
        }

        var named = request.MemberIds.Distinct().ToList();
        return named.Count > 0 && named.All(members.Contains) ? named : null;
    }
}

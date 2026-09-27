using System.Security.Claims;
using BallBank.Api.Features.Membership;
using BallBank.Domain.Membership;
using BallBank.Domain.Treasury;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace BallBank.Api.Features.Treasury;

/// <summary>The body of <c>POST /leagues/{leagueId}/accounts/{accountId}/adjustments</c>.</summary>
/// <param name="AdjustmentId">Chosen by the client, so the same adjustment sent twice is recorded once.</param>
/// <param name="Amount">Signed: positive raises what the member owes, negative lowers it.</param>
/// <param name="Reason">Why, for the member to read on their statement; required.</param>
/// <param name="Version">The account's version the treasurer last saw (ADR-0005); required.</param>
public sealed record PostAdjustmentRequest(Guid AdjustmentId, decimal Amount, string? Reason, int? Version);

public static class PostAdjustmentEndpoint
{
    /// <summary>
    /// A treasurer corrects a balance with a reason: a waiver, a refund of an overpayment, a fix. Answers
    /// with the statement as it now stands. The same adjustment id again records nothing and answers with
    /// the statement.
    /// </summary>
    [Authorize(Policy = Policies.LeagueTreasurer)]
    [WolverinePost("/leagues/{leagueId}/accounts/{accountId}/adjustments")]
    public static async Task<IResult> Post(
        Guid leagueId,
        Guid accountId,
        PostAdjustmentRequest request,
        IdempotentRequest idempotency,
        ClaimsPrincipal user,
        IDocumentStore store,
        CancellationToken cancellation)
    {
        if (request.Version is not { } expectedVersion)
        {
            return ExpectedVersion.Required();
        }

        // Committed explicitly below, so anything recorded on the account since this was read collides rather than interleaves.
        await using var session = store.LightweightSession(leagueId.ToString());

        var league = await session.Events.AggregateStreamAsync<League>(leagueId, token: cancellation);
        var stream = await session.Events.FetchForWriting<MemberAccount>(accountId, cancellation);
        var statement = await session.LoadAsync<MemberStatement>(accountId, cancellation);
        if (league is null || stream.Aggregate is not { } account || statement is null)
        {
            return Results.NotFound();
        }

        // The policy asked the caller's memberships; the league, the source of truth, is asked again
        // (ADR-0011), which also gives the acting member id.
        if (league.MemberHeldBy(user.Subject()) is not { IsTreasurer: true } treasurer)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Not a treasurer",
                detail: $"Only a treasurer of {league.Name} can post an adjustment.");
        }

        var posted = account.PostAdjustment(
            new PostAdjustment(accountId, request.AdjustmentId, request.Amount, request.Reason ?? string.Empty, treasurer.MemberId),
            DateTimeOffset.UtcNow);

        // Already recorded: a double submission adds nothing, whatever version it carries. A retry of this
        // very request that committed after the middleware looked answers as it did then.
        if (posted is null)
        {
            return await idempotency.ReplayAsync(session, cancellation)
                ?? Results.Ok(AccountStatement.Of(statement, league, treasurer.MemberId));
        }

        if (stream.CurrentVersion != expectedVersion)
        {
            return ExpectedVersion.Conflict(stream.CurrentVersion ?? 0);
        }

        stream.AppendOne(posted);
        session.SetHeader(EventHeaders.Subject, user.Subject());

        // The statement as the inline projection will leave it once this commits.
        var fresh = MemberStatementProjection.Evolve(statement, posted)!;
        fresh.Version = expectedVersion + 1;

        // An attestation, or another treasurer's decision, may have been recorded since the check above.
        return await idempotency.CommitAsync(
            session,
            StatusCodes.Status201Created,
            AccountStatement.Of(fresh, league, treasurer.MemberId),
            onCollision: async committed =>
                ExpectedVersion.Conflict((await committed.Events.FetchStreamStateAsync(accountId, cancellation))?.Version ?? 0),
            cancellation);
    }
}

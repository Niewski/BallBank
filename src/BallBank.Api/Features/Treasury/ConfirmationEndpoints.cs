using System.Security.Claims;
using BallBank.Api.Features.Membership;
using BallBank.Domain.Membership;
using BallBank.Domain.Treasury;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace BallBank.Api.Features.Treasury;

/// <summary>The body of <c>POST …/attestations/{attestationId}/confirmation</c>.</summary>
/// <param name="Version">The account's version the treasurer last saw (ADR-0005); required.</param>
public sealed record ConfirmPaymentRequest(int? Version);

/// <summary>The body of <c>POST …/attestations/{attestationId}/rejection</c>.</summary>
/// <param name="Reason">Why, for the member to read on their statement; required.</param>
/// <param name="Version">The account's version the treasurer last saw (ADR-0005); required.</param>
public sealed record RejectPaymentRequest(string? Reason, int? Version);

public static class ConfirmationEndpoints
{
    /// <summary>
    /// A treasurer confirms an attested payment arrived, and the balance moves. Answers with the
    /// statement as it now stands. Confirming it again records nothing and answers with the statement.
    /// </summary>
    [Authorize(Policy = Policies.LeagueTreasurer)]
    [WolverinePost("/leagues/{leagueId}/accounts/{accountId}/attestations/{attestationId}/confirmation")]
    public static Task<IResult> Confirm(
        Guid leagueId,
        Guid accountId,
        Guid attestationId,
        ConfirmPaymentRequest request,
        IdempotentRequest idempotency,
        ClaimsPrincipal user,
        IDocumentStore store,
        CancellationToken cancellation) =>
        Settle(
            leagueId, accountId, request.Version, idempotency, user, store, cancellation,
            (account, treasurer, now) => account.Confirm(new ConfirmPayment(accountId, attestationId, treasurer), now));

    /// <summary>
    /// A treasurer rejects an attested payment, with a reason the member sees on their statement. The
    /// balance stays; the member can attest again. Rejecting it again records nothing.
    /// </summary>
    [Authorize(Policy = Policies.LeagueTreasurer)]
    [WolverinePost("/leagues/{leagueId}/accounts/{accountId}/attestations/{attestationId}/rejection")]
    public static Task<IResult> Reject(
        Guid leagueId,
        Guid accountId,
        Guid attestationId,
        RejectPaymentRequest request,
        IdempotentRequest idempotency,
        ClaimsPrincipal user,
        IDocumentStore store,
        CancellationToken cancellation) =>
        Settle(
            leagueId, accountId, request.Version, idempotency, user, store, cancellation,
            (account, treasurer, now) => account.Reject(new RejectPayment(accountId, attestationId, treasurer, request.Reason ?? string.Empty), now));

    // Confirming and rejecting differ only in what the account decides.
    private static async Task<IResult> Settle(
        Guid leagueId,
        Guid accountId,
        int? version,
        IdempotentRequest idempotency,
        ClaimsPrincipal user,
        IDocumentStore store,
        CancellationToken cancellation,
        Func<MemberAccount, Guid, DateTimeOffset, object?> decide)
    {
        if (version is not { } expectedVersion)
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
                detail: $"Only a treasurer of {league.Name} can confirm or reject a payment.");
        }

        var settled = decide(account, treasurer.MemberId, DateTimeOffset.UtcNow);

        // Already settled this way: a second click adds nothing, whatever version it carries. A retry of
        // this very request that committed after the middleware looked answers as it did then.
        if (settled is null)
        {
            return await idempotency.ReplayAsync(session, cancellation)
                ?? Results.Ok(AccountStatement.Of(statement, league, treasurer.MemberId));
        }

        if (stream.CurrentVersion != expectedVersion)
        {
            return ExpectedVersion.Conflict(stream.CurrentVersion ?? 0);
        }

        stream.AppendOne(settled);
        session.SetHeader(EventHeaders.Subject, user.Subject());

        // The statement as the inline projection will leave it once this commits.
        var fresh = MemberStatementProjection.Evolve(statement, settled)!;
        fresh.Version = expectedVersion + 1;

        // An attestation, or another treasurer's decision, may have been recorded since the check above.
        return await idempotency.CommitAsync(
            session,
            StatusCodes.Status200OK,
            AccountStatement.Of(fresh, league, treasurer.MemberId),
            onCollision: async committed =>
                ExpectedVersion.Conflict((await committed.Events.FetchStreamStateAsync(accountId, cancellation))?.Version ?? 0),
            cancellation);
    }
}

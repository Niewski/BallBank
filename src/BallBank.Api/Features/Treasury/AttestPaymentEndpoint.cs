using System.Security.Claims;
using System.Text.Json.Serialization;
using BallBank.Api.Features.Membership;
using BallBank.Domain.Membership;
using BallBank.Domain.Treasury;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace BallBank.Api.Features.Treasury;

/// <summary>The body of <c>POST /leagues/{leagueId}/accounts/{accountId}/attestations</c>.</summary>
/// <param name="AttestationId">Chosen by the client, so the same attestation sent twice is recorded once.</param>
/// <param name="Rail">By name: <c>"Cash"</c>, <c>"Venmo"</c> and so on.</param>
/// <param name="Version">The account's version the person last saw (ADR-0005); required.</param>
public sealed record AttestPaymentRequest(
    Guid AttestationId,
    decimal Amount,
    [property: JsonConverter(typeof(JsonStringEnumConverter<PaymentRail>))] PaymentRail Rail,
    string? Reference,
    int? Version);

public static class AttestPaymentEndpoint
{
    /// <summary>
    /// "I paid": the account's own member, or a treasurer handed the money, attests a payment. It shows as
    /// pending and the balance does not move until a treasurer confirms it. Answers with the statement as
    /// it now stands. The same attestation id again records nothing and answers with the statement.
    /// </summary>
    [Authorize(Policy = Policies.LeagueMember)]
    [WolverinePost("/leagues/{leagueId}/accounts/{accountId}/attestations")]
    public static async Task<IResult> Post(
        Guid leagueId,
        Guid accountId,
        AttestPaymentRequest request,
        IdempotentRequest idempotency,
        ClaimsPrincipal user,
        IDocumentStore store,
        TimeProvider clock,
        CancellationToken cancellation)
    {
        if (request.Version is not { } expectedVersion)
        {
            return ExpectedVersion.Required();
        }

        // Committed explicitly below, so an attestation recorded since this was read collides rather than interleaves.
        await using var session = store.LightweightSession(leagueId.ToString());

        var league = await session.Events.AggregateStreamAsync<League>(leagueId, token: cancellation);
        var stream = await session.Events.FetchForWriting<MemberAccount>(accountId, cancellation);
        var statement = await session.LoadAsync<MemberStatement>(accountId, cancellation);
        if (league is null || stream.Aggregate is not { } account || statement is null)
        {
            return Results.NotFound();
        }

        // Asked of the league, the source of truth (ADR-0011), which also gives the acting member id.
        if (league.MemberHeldBy(user.Subject()) is not { } caller || !(caller.IsTreasurer || caller.MemberId == account.MemberId))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Not your account",
                detail: "Only a treasurer, or the account's own member, can attest a payment on it.");
        }

        var attested = account.Attest(
            new AttestPayment(accountId, request.AttestationId, request.Amount, request.Rail, request.Reference, caller.MemberId),
            clock.GetUtcNow());

        // Already recorded: a double submission adds nothing, whatever version it carries. A retry of this
        // very request that committed after the middleware looked answers as it did then.
        if (attested is null)
        {
            return await idempotency.ReplayAsync(session, cancellation)
                ?? Results.Ok(AccountStatement.Of(statement, league, caller.MemberId));
        }

        if (stream.CurrentVersion != expectedVersion)
        {
            return ExpectedVersion.Conflict(stream.CurrentVersion ?? 0);
        }

        stream.AppendOne(attested);
        session.SetHeader(EventHeaders.Subject, user.Subject());

        // The statement as the inline projection will leave it once this commits.
        var fresh = MemberStatementProjection.Evolve(statement, attested)!;
        fresh.Version = expectedVersion + 1;

        // Another attestation, or a treasurer's confirmation, may have been recorded since the check above.
        return await idempotency.CommitAsync(
            session,
            StatusCodes.Status201Created,
            AccountStatement.Of(fresh, league, caller.MemberId),
            onCollision: async committed =>
                ExpectedVersion.Conflict((await committed.Events.FetchStreamStateAsync(accountId, cancellation))?.Version ?? 0),
            cancellation);
    }
}

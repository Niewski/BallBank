using System.Security.Claims;
using BallBank.Api.Features.Membership;
using BallBank.Domain.Membership;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace BallBank.Api.Features.Treasury;

/// <summary>What <c>GET /leagues/{leagueId}/accounts/{accountId}</c> answers: one account's statement.</summary>
/// <param name="Balance">Positive: the member owes the pot. Negative: the pot owes the member.</param>
/// <param name="Version">The account's stream version, for a command about it.</param>
/// <param name="Yours">The caller holds the account's member, rather than reading it as a treasurer.</param>
/// <param name="YouAreTreasurer">The caller is a treasurer of the league, so can confirm or reject its attestations.</param>
public sealed record AccountStatement(
    Guid AccountId,
    string Season,
    Guid MemberId,
    string TeamName,
    string? DisplayName,
    bool Yours,
    bool YouAreTreasurer,
    decimal Balance,
    StatementTotals Totals,
    IReadOnlyList<StatementEntry> Lines,
    int Version)
{
    /// <summary>
    /// <paramref name="statement"/> as served to the member <paramref name="callerMemberId"/>, with the balance and
    /// totals computed now and every acting member's name alongside their id.
    /// </summary>
    public static AccountStatement Of(MemberStatement statement, League league, Guid callerMemberId)
    {
        var members = league.Members.ToDictionary(m => m.MemberId);
        var member = members[statement.MemberId];
        var totals = statement.Totals();

        return new AccountStatement(
            statement.Id,
            statement.Season,
            member.MemberId,
            member.TeamName,
            MemberNames.DisplayName(member),
            callerMemberId == statement.MemberId,
            members.TryGetValue(callerMemberId, out var caller) && caller.IsTreasurer,
            totals.Balance,
            totals,
            statement.Lines
                .Select(line => new StatementEntry(
                    line.Kind,
                    line.Id,
                    line.Amount,
                    line.By,
                    MemberNames.ActingName(members, line.By),
                    line.At,
                    line.Memo,
                    line.DueDate,
                    line.Rail?.ToString(),
                    line.Reference,
                    line.Status?.ToString(),
                    line.Reason,
                    line.Refund))
                .ToArray(),
            statement.Version);
    }
}

/// <summary>One line of a statement as served, with the acting member's name alongside their id.</summary>
/// <param name="Kind">An <see cref="StatementLineKind"/>.</param>
/// <param name="Status">An attestation's: Pending, Confirmed or Rejected; <c>null</c> for an assessment.</param>
/// <param name="Amount">Signed for an adjustment: positive raised the balance, negative lowered it.</param>
/// <param name="Reason">Why an attestation was rejected, or why an adjustment was posted.</param>
/// <param name="Refund">An adjustment that paid the member back out of the pot.</param>
public sealed record StatementEntry(
    string Kind,
    Guid Id,
    decimal Amount,
    Guid By,
    string? ByName,
    DateTimeOffset At,
    string? Memo,
    DateOnly? DueDate,
    string? Rail,
    string? Reference,
    string? Status,
    string? Reason,
    bool Refund);

public static class StatementEndpoint
{
    /// <summary>
    /// One account's statement, for a treasurer of the league or the account's own member: its balance
    /// and totals, computed now from its line items, and every line in the order it happened.
    /// </summary>
    [Authorize(Policy = Policies.LeagueMember)]
    [WolverineGet("/leagues/{leagueId}/accounts/{accountId}")]
    public static async Task<IResult> Get(
        Guid leagueId,
        Guid accountId,
        ClaimsPrincipal user,
        IQuerySession session,
        IDocumentStore store,
        CancellationToken cancellation)
    {
        var statement = await session.LoadAsync<MemberStatement>(accountId, cancellation);
        var league = await session.Events.AggregateStreamAsync<League>(leagueId, token: cancellation);
        if (statement is null || league is null)
        {
            return Results.NotFound();
        }

        if (await AccountReaders.CallerAsync(store, user, leagueId, statement.MemberId, cancellation) is not { } caller)
        {
            return AccountReaders.NotYourAccount("statement");
        }

        return Results.Ok(AccountStatement.Of(statement, league, caller.MemberId));
    }
}

/// <summary>Who may read an account: a treasurer of its league, or its own member.</summary>
public static class AccountReaders
{
    /// <summary>
    /// The caller's membership of the league when it lets them read the account of <paramref name="memberId"/>,
    /// else <c>null</c>. The policy knows the caller is a member; whose account this is only the account knows.
    /// </summary>
    public static async Task<LeagueMembership?> CallerAsync(
        IDocumentStore store, ClaimsPrincipal user, Guid leagueId, Guid memberId, CancellationToken cancellation)
    {
        // UserMemberships lives in the default tenant, not the league's (ADR-0011).
        await using var memberships = store.QuerySession();
        var caller = (await memberships.LoadAsync<UserMemberships>(user.Subject(), cancellation))?
            .Leagues.FirstOrDefault(l => l.LeagueId == leagueId);
        return caller is not null && (caller.Roles.Contains(Roles.Treasurer) || caller.MemberId == memberId) ? caller : null;
    }

    /// <summary>The refusal for a caller <see cref="CallerAsync"/> turned away from the account's <paramref name="what"/>.</summary>
    public static IResult NotYourAccount(string what) => Results.Problem(
        statusCode: StatusCodes.Status403Forbidden,
        title: "Not your account",
        detail: $"Only a treasurer, or the account's own member, can read its {what}.");
}

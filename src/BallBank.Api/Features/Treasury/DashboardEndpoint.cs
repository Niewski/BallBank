using BallBank.Api.Features.Membership;
using BallBank.Domain.Membership;
using BallBank.Domain.Treasury;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace BallBank.Api.Features.Treasury;

/// <summary>What a season's accounts add up to. <c>Owed</c> is what the pot owes members, as a positive amount.</summary>
public sealed record DashboardFigures(
    decimal Assessed,
    decimal Confirmed,
    decimal Refunded,
    decimal Adjusted,
    decimal Pot,
    decimal Outstanding,
    decimal Owed,
    int PendingAttestations);

/// <summary>A member who still owes after the earliest date anything was due.</summary>
/// <param name="DisplayName">What the member goes by; <c>null</c> when nothing is known.</param>
/// <param name="EarliestDueDate">The earliest date anything assessed to the account was due.</param>
public sealed record Delinquent(
    Guid AccountId,
    Guid MemberId,
    string TeamName,
    string? DisplayName,
    decimal Balance,
    DateOnly EarliestDueDate,
    int DaysOverdue);

/// <summary>The treasurer's dashboard for one season.</summary>
/// <param name="AsOf">When the last event it reflects was recorded; <c>null</c> until the projection has built it.</param>
public sealed record Dashboard(string Season, DashboardFigures Figures, Delinquent[] Delinquents, DateTimeOffset? AsOf);

public static class DashboardEndpoint
{
    /// <summary>
    /// The season's figures and delinquents, read from <see cref="LeaguePot"/> (ADR-0006): built by the
    /// projection daemon, so it can trail a command by a moment, which <c>AsOf</c> says. Days overdue
    /// count from the UTC date of the API's clock.
    /// </summary>
    [Authorize(Policy = Policies.LeagueTreasurer)]
    [WolverineGet("/leagues/{leagueId}/seasons/{season}/dashboard")]
    public static async Task<IResult> Get(Guid leagueId, string season, IQuerySession session, TimeProvider clock, CancellationToken cancellation)
    {
        var seasonId = SeasonIds.SeasonId(leagueId, season);
        var listing = await session.LoadAsync<SeasonListing>(seasonId, cancellation);
        var league = await session.Events.AggregateStreamAsync<League>(leagueId, token: cancellation);
        if (listing is null || league is null)
        {
            return Results.NotFound();
        }

        var pot = await session.LoadAsync<LeaguePot>(seasonId, cancellation);
        if (pot is null)
        {
            return Results.Ok(new Dashboard(listing.Label, new DashboardFigures(0, 0, 0, 0, 0, 0, 0, 0), [], AsOf: null));
        }

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var members = league.Members.ToDictionary(m => m.MemberId);

        var delinquents = pot.Accounts
            .Select(account => (Account: account, DaysOverdue: Delinquency.DaysOverdue(account.Balance, account.EarliestDueDate, today)))
            .Where(late => late.DaysOverdue is not null && members.ContainsKey(late.Account.MemberId))
            .Select(late =>
            {
                var member = members[late.Account.MemberId];
                return new Delinquent(
                    late.Account.AccountId,
                    member.MemberId,
                    member.TeamName,
                    MemberNames.DisplayName(member),
                    late.Account.Balance,
                    late.Account.EarliestDueDate!.Value,
                    late.DaysOverdue!.Value);
            })
            .OrderByDescending(d => d.DaysOverdue)
            .ThenByDescending(d => d.Balance)
            .ThenBy(d => d.TeamName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Results.Ok(new Dashboard(
            listing.Label,
            new DashboardFigures(pot.Assessed, pot.Confirmed, pot.Refunded, pot.Adjusted, pot.Pot, pot.Outstanding, pot.Owed, pot.PendingAttestations),
            delinquents,
            pot.AsOf));
    }
}

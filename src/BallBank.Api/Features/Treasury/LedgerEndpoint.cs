using BallBank.Api.Features.Membership;
using BallBank.Domain.Membership;
using BallBank.Domain.Treasury;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace BallBank.Api.Features.Treasury;

/// <summary>One account on a season's ledger.</summary>
/// <param name="DisplayName">What the member goes by: its claimant's name, else its Sleeper name; <c>null</c> when neither is known.</param>
/// <param name="Balance">Positive: the member owes the pot. Negative: the pot owes the member.</param>
/// <param name="Version">The account's stream version, for a command about it.</param>
public sealed record LedgerEntry(
    Guid AccountId,
    Guid MemberId,
    string TeamName,
    string? DisplayName,
    decimal Balance,
    int PendingAttestations,
    int Version);

public static class LedgerEndpoint
{
    /// <summary>
    /// Every account of the season, for any member of the league, in team name order: a query over
    /// the season's statements, with names from the league's member list.
    /// </summary>
    [Authorize(Policy = Policies.LeagueMember)]
    [WolverineGet("/leagues/{leagueId}/seasons/{season}/ledger")]
    public static async Task<IResult> Get(Guid leagueId, string season, IQuerySession session, CancellationToken cancellation)
    {
        var listing = await session.LoadAsync<SeasonListing>(SeasonIds.SeasonId(leagueId, season), cancellation);
        var league = await session.Events.AggregateStreamAsync<League>(leagueId, token: cancellation);
        if (listing is null || league is null)
        {
            return Results.NotFound();
        }

        var statements = await session.Query<MemberStatement>()
            .Where(s => s.Season == listing.Label)
            .ToListAsync(cancellation);
        var members = league.Members.ToDictionary(m => m.MemberId);

        return Results.Ok(statements
            .Select(statement => (Statement: statement, Member: members[statement.MemberId]))
            .OrderBy(account => account.Member.TeamName, StringComparer.OrdinalIgnoreCase)
            .Select(account => new LedgerEntry(
                account.Statement.Id,
                account.Member.MemberId,
                account.Member.TeamName,
                MemberNames.DisplayName(account.Member),
                account.Statement.Totals().Balance,
                account.Statement.PendingAttestations(),
                account.Statement.Version))
            .ToArray());
    }
}

public static class MemberNames
{
    /// <summary>What a member goes by: its claimant's name, else its Sleeper name.</summary>
    public static string? DisplayName(Member member) => member.HolderDisplayName ?? member.SleeperDisplayName;
}

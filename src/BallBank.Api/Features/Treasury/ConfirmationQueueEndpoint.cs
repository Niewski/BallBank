using BallBank.Api.Features.Membership;
using BallBank.Domain.Membership;
using BallBank.Domain.Treasury;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace BallBank.Api.Features.Treasury;

/// <summary>One pending attestation in a season's confirmation queue.</summary>
/// <param name="DisplayName">What the account's member goes by; <c>null</c> when nothing is known.</param>
/// <param name="AttestedByName">What the member who attested goes by: the account's own, or a treasurer handed the money.</param>
/// <param name="Version">The account's stream version, for confirming or rejecting.</param>
public sealed record ConfirmationQueueEntry(
    Guid AccountId,
    Guid AttestationId,
    Guid MemberId,
    string TeamName,
    string? DisplayName,
    decimal Amount,
    string? Rail,
    string? Reference,
    Guid AttestedBy,
    string? AttestedByName,
    DateTimeOffset AttestedAt,
    int Version);

public static class ConfirmationQueueEndpoint
{
    /// <summary>
    /// Every attestation of the season still waiting for a treasurer, oldest first: a query over the
    /// season's statements (ADR-0006), with names from the league's member list.
    /// </summary>
    [Authorize(Policy = Policies.LeagueTreasurer)]
    [WolverineGet("/leagues/{leagueId}/seasons/{season}/confirmations")]
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
            .SelectMany(statement => statement.Lines
                .Where(line => line is { Kind: StatementLineKind.Attestation, Status: AttestationStatus.Pending })
                .Select(line => (Statement: statement, Line: line)))
            .OrderBy(pending => pending.Line.At)
            .Select(pending =>
            {
                var member = members[pending.Statement.MemberId];
                return new ConfirmationQueueEntry(
                    pending.Statement.Id,
                    pending.Line.Id,
                    member.MemberId,
                    member.TeamName,
                    MemberNames.DisplayName(member),
                    pending.Line.Amount,
                    pending.Line.Rail?.ToString(),
                    pending.Line.Reference,
                    pending.Line.By,
                    MemberNames.ActingName(members, pending.Line.By),
                    pending.Line.At,
                    pending.Statement.Version);
            })
            .ToArray());
    }
}

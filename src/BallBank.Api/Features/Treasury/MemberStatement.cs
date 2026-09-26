using BallBank.Domain.Treasury;
using JasperFx.Events;
using Marten.Events.Aggregation;

namespace BallBank.Api.Features.Treasury;

/// <summary>
/// The read model of one account: for now, its assessments. Tenant-scoped like every document, keyed
/// by account id. Built inline, in the same transaction as the events it reads, so it exists as soon
/// as the account does — unlike <see cref="MemberAccountProjection"/>, which rebuilds on every read.
/// </summary>
public sealed class MemberStatement
{
    /// <summary>The account id.</summary>
    public Guid Id { get; set; }

    /// <summary>Stream version, set by the event store.</summary>
    public int Version { get; set; }

    public Guid LeagueId { get; set; }
    public string Season { get; set; } = string.Empty;
    public Guid MemberId { get; set; }
    public List<StatementLine> Lines { get; set; } = [];
}

/// <summary>One line of a statement: an assessment, for now (ADR: attestations and payouts join it later).</summary>
public sealed record StatementLine(string Memo, decimal Amount, DateOnly DueDate, Guid By, DateTimeOffset At);

/// <summary>
/// Builds <see cref="MemberStatement"/> from a <see cref="MemberAccount"/> stream, wired explicitly for
/// the same reason as <see cref="MemberAccountProjection"/>.
/// </summary>
public sealed class MemberStatementProjection : SingleStreamProjection<MemberStatement, Guid>
{
    public override MemberStatement? Evolve(MemberStatement? snapshot, Guid id, IEvent e)
    {
        switch (e.Data)
        {
            case AccountOpened opened:
                return new MemberStatement
                {
                    Id = opened.AccountId,
                    LeagueId = opened.LeagueId,
                    Season = opened.Season,
                    MemberId = opened.MemberId,
                };
            case DuesAssessed assessed when snapshot is not null:
                snapshot.Lines.Add(new StatementLine(assessed.Memo, assessed.Amount, assessed.DueDate, assessed.AssessedBy, assessed.AssessedAt));
                return snapshot;
            default:
                return snapshot;
        }
    }
}

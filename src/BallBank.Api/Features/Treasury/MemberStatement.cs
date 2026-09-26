using BallBank.Domain.Treasury;
using JasperFx.Events;
using Marten.Events.Aggregation;

namespace BallBank.Api.Features.Treasury;

/// <summary>
/// The read model of one account: its line items, in order. Tenant-scoped like every document, keyed
/// by account id. Built inline, in the same transaction as the events it reads, so it exists as soon
/// as the account does — unlike <see cref="MemberAccountProjection"/>, which rebuilds on every read.
/// Totals and the balance are not stored; <see cref="Totals"/> computes them from the lines (rule 3).
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

    /// <summary>What the lines add up to. Only a confirmed attestation counts against what is owed.</summary>
    public StatementTotals Totals() => new(
        Assessed: Lines.Where(l => l.Kind == StatementLineKind.Assessment).Sum(l => l.Amount),
        Confirmed: Lines.Where(l => l is { Kind: StatementLineKind.Attestation, Status: AttestationStatus.Confirmed }).Sum(l => l.Amount));

    /// <summary>How many attestations wait for a treasurer to confirm or reject them.</summary>
    public int PendingAttestations() =>
        Lines.Count(l => l is { Kind: StatementLineKind.Attestation, Status: AttestationStatus.Pending });
}

/// <summary>
/// One line of a statement: an assessment (memo, due date) or an attestation (rail, reference, status,
/// and the reason when rejected). Adjustments and payouts join it later.
/// </summary>
/// <param name="Id">The assessment or attestation id.</param>
/// <param name="By">The acting member: who assessed, or who attested.</param>
public sealed record StatementLine(
    string Kind,
    Guid Id,
    decimal Amount,
    Guid By,
    DateTimeOffset At,
    string? Memo = null,
    DateOnly? DueDate = null,
    PaymentRail? Rail = null,
    string? Reference = null,
    AttestationStatus? Status = null,
    string? Reason = null);

public static class StatementLineKind
{
    public const string Assessment = "Assessment";
    public const string Attestation = "Attestation";
}

/// <summary>What a statement's lines add up to, computed whenever it is served.</summary>
public sealed record StatementTotals(decimal Assessed, decimal Confirmed)
{
    /// <summary>Positive: the member owes the pot. Negative: the pot owes the member.</summary>
    public decimal Balance => Assessed - Confirmed;
}

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
                snapshot.Lines.Add(new StatementLine(
                    StatementLineKind.Assessment, assessed.AssessmentId, assessed.Amount, assessed.AssessedBy, assessed.AssessedAt,
                    Memo: assessed.Memo, DueDate: assessed.DueDate));
                return snapshot;
            case PaymentAttested attested when snapshot is not null:
                snapshot.Lines.Add(new StatementLine(
                    StatementLineKind.Attestation, attested.AttestationId, attested.Amount, attested.AttestedBy, attested.AttestedAt,
                    Rail: attested.Rail, Reference: attested.Reference, Status: AttestationStatus.Pending));
                return snapshot;
            case PaymentConfirmed confirmed when snapshot is not null:
                Decide(snapshot, confirmed.AttestationId, line => line with { Status = AttestationStatus.Confirmed });
                return snapshot;
            case PaymentRejected rejected when snapshot is not null:
                Decide(snapshot, rejected.AttestationId, line => line with { Status = AttestationStatus.Rejected, Reason = rejected.Reason });
                return snapshot;
            default:
                return snapshot;
        }
    }

    private static void Decide(MemberStatement statement, Guid attestationId, Func<StatementLine, StatementLine> decision)
    {
        var index = statement.Lines.FindIndex(l => l.Kind == StatementLineKind.Attestation && l.Id == attestationId);
        if (index >= 0)
        {
            statement.Lines[index] = decision(statement.Lines[index]);
        }
    }
}

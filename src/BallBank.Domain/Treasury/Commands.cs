namespace BallBank.Domain.Treasury;

// Commands are requests. The aggregate decides whether each one becomes an event.
// Ids are supplied by the caller so a retried command is recognised and replays as a no-op.

public sealed record OpenAccount(Guid AccountId, Guid LeagueId, string Season, Guid MemberId);

public sealed record AssessDues(
    Guid AccountId,
    Guid AssessmentId,
    decimal Amount,
    DateOnly DueDate,
    string Memo,
    Guid AssessedBy);

public sealed record AttestPayment(
    Guid AccountId,
    Guid AttestationId,
    decimal Amount,
    PaymentRail Rail,
    string? Reference,
    Guid AttestedBy);

public sealed record ConfirmPayment(Guid AccountId, Guid AttestationId, Guid ConfirmedBy);

public sealed record RejectPayment(Guid AccountId, Guid AttestationId, Guid RejectedBy, string Reason);

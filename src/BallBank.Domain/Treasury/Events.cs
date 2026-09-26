namespace BallBank.Domain.Treasury;

// Events are facts that already happened. Past tense, immutable, never edited once released:
// a change in shape is a new event type, not a modified one (see docs/domain.md).

/// <summary>A member's books were opened for one season of one league.</summary>
public sealed record AccountOpened(
    Guid AccountId,
    Guid LeagueId,
    string Season,
    Guid MemberId,
    DateTimeOffset OpenedAt);

/// <summary>A treasurer opened a season: every current member owes its dues by its due date.</summary>
public sealed record SeasonOpened(
    Guid SeasonId,
    Guid LeagueId,
    string Label,
    decimal DuesAmount,
    DateOnly DueDate,
    Guid OpenedBy,
    DateTimeOffset OpenedAt);

/// <summary>The treasurer assessed an amount the member owes.</summary>
public sealed record DuesAssessed(
    Guid AssessmentId,
    decimal Amount,
    DateOnly DueDate,
    string Memo,
    Guid AssessedBy,
    DateTimeOffset AssessedAt);

/// <summary>A member said they paid, and how. Not yet counted against the balance.</summary>
public sealed record PaymentAttested(
    Guid AttestationId,
    decimal Amount,
    PaymentRail Rail,
    string? Reference,
    Guid AttestedBy,
    DateTimeOffset AttestedAt);

/// <summary>The treasurer confirmed the attested payment arrived. Now it counts.</summary>
public sealed record PaymentConfirmed(
    Guid AttestationId,
    Guid ConfirmedBy,
    DateTimeOffset ConfirmedAt);

/// <summary>The treasurer could not find or did not accept the attested payment.</summary>
public sealed record PaymentRejected(
    Guid AttestationId,
    Guid RejectedBy,
    string Reason,
    DateTimeOffset RejectedAt);

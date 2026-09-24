namespace BallBank.Domain.Treasury;

public enum AttestationStatus
{
    Pending,
    Confirmed,
    Rejected,
}

/// <summary>A member's claim that they paid, and where the treasurer left it.</summary>
public sealed record Attestation(
    Guid AttestationId,
    decimal Amount,
    PaymentRail Rail,
    string? Reference,
    Guid AttestedBy,
    DateTimeOffset AttestedAt,
    AttestationStatus Status);

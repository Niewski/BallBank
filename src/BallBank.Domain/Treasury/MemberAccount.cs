namespace BallBank.Domain.Treasury;

/// <summary>
/// One member's books for one season of one league. Event-sourced: state is rebuilt by applying
/// events; decisions validate a command against that state and return the resulting event (or
/// <c>null</c> when the command has already been applied, which keeps retries harmless).
/// Balances are derived, never stored.
/// </summary>
public sealed class MemberAccount
{
    private readonly Dictionary<Guid, decimal> _assessments = new();
    private readonly Dictionary<Guid, Attestation> _attestations = new();

    /// <summary>Stream identity. Public setter so the event store can assign it during aggregation.</summary>
    public Guid Id { get; set; }

    /// <summary>Stream version, set by the event store; used for optimistic concurrency.</summary>
    public int Version { get; set; }

    public Guid LeagueId { get; private set; }
    public string Season { get; private set; } = string.Empty;
    public Guid MemberId { get; private set; }

    public decimal Assessed => _assessments.Values.Sum();
    public decimal Confirmed { get; private set; }
    public decimal Balance => Assessed - Confirmed;

    public IReadOnlyCollection<Attestation> Attestations => _attestations.Values;
    public IEnumerable<Attestation> PendingAttestations =>
        _attestations.Values.Where(a => a.Status == AttestationStatus.Pending);

    // ---------------------------------------------------------------------------------------------
    // Decisions. Pure: read state, return an event or throw DomainException. Never mutate here.
    // ---------------------------------------------------------------------------------------------

    public static AccountOpened Open(OpenAccount command, DateTimeOffset now)
    {
        if (command.AccountId == Guid.Empty)
        {
            throw new DomainException("An account needs an id.");
        }

        if (string.IsNullOrWhiteSpace(command.Season))
        {
            throw new DomainException("An account belongs to a season.");
        }

        return new AccountOpened(command.AccountId, command.LeagueId, command.Season.Trim(), command.MemberId, now);
    }

    /// <summary>Returns <c>null</c> when this assessment id was already recorded (idempotent replay).</summary>
    public DuesAssessed? Assess(AssessDues command, DateTimeOffset now)
    {
        if (_assessments.ContainsKey(command.AssessmentId))
        {
            return null;
        }

        RequirePositive(command.Amount, "An assessment");

        return new DuesAssessed(
            command.AssessmentId,
            command.Amount,
            command.DueDate,
            command.Memo?.Trim() ?? string.Empty,
            command.AssessedBy,
            now);
    }

    /// <summary>Returns <c>null</c> when this attestation id was already recorded (idempotent replay).</summary>
    public PaymentAttested? Attest(AttestPayment command, DateTimeOffset now)
    {
        if (_attestations.ContainsKey(command.AttestationId))
        {
            return null;
        }

        RequirePositive(command.Amount, "A payment");

        if (command.Rail != PaymentRail.Cash && string.IsNullOrWhiteSpace(command.Reference))
        {
            throw new DomainException(
                $"A {command.Rail} payment needs a reference so the treasurer can find it.");
        }

        return new PaymentAttested(
            command.AttestationId,
            command.Amount,
            command.Rail,
            command.Reference?.Trim(),
            command.AttestedBy,
            now);
    }

    /// <summary>Returns <c>null</c> when the attestation is already confirmed (idempotent replay).</summary>
    public PaymentConfirmed? Confirm(ConfirmPayment command, DateTimeOffset now)
    {
        var attestation = RequireAttestation(command.AttestationId);

        return attestation.Status switch
        {
            AttestationStatus.Confirmed => null,
            AttestationStatus.Rejected => throw new DomainException(
                "A rejected attestation cannot be confirmed; ask the member to attest the payment again."),
            _ => new PaymentConfirmed(command.AttestationId, command.ConfirmedBy, now),
        };
    }

    /// <summary>Returns <c>null</c> when the attestation is already rejected (idempotent replay).</summary>
    public PaymentRejected? Reject(RejectPayment command, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            throw new DomainException("Rejecting a payment needs a reason the member will see.");
        }

        var attestation = RequireAttestation(command.AttestationId);

        return attestation.Status switch
        {
            AttestationStatus.Rejected => null,
            AttestationStatus.Confirmed => throw new DomainException(
                "A confirmed payment cannot be rejected; post an adjustment instead."),
            _ => new PaymentRejected(command.AttestationId, command.RejectedBy, command.Reason.Trim(), now),
        };
    }

    // ---------------------------------------------------------------------------------------------
    // Evolution. How each event changes state. Not named Apply/Create: Marten treats those names as its
    // own conventions, which need a source generator this package-free assembly cannot run.
    // ---------------------------------------------------------------------------------------------

    private void When(AccountOpened @event)
    {
        Id = @event.AccountId;
        LeagueId = @event.LeagueId;
        Season = @event.Season;
        MemberId = @event.MemberId;
    }

    private void When(DuesAssessed @event) => _assessments[@event.AssessmentId] = @event.Amount;

    private void When(PaymentAttested @event) =>
        _attestations[@event.AttestationId] = new Attestation(
            @event.AttestationId,
            @event.Amount,
            @event.Rail,
            @event.Reference,
            @event.AttestedBy,
            @event.AttestedAt,
            AttestationStatus.Pending);

    private void When(PaymentConfirmed @event)
    {
        var attestation = _attestations[@event.AttestationId];
        _attestations[@event.AttestationId] = attestation with { Status = AttestationStatus.Confirmed };
        Confirmed += attestation.Amount;
    }

    private void When(PaymentRejected @event)
    {
        var attestation = _attestations[@event.AttestationId];
        _attestations[@event.AttestationId] = attestation with { Status = AttestationStatus.Rejected };
    }

    /// <summary>Applies any event of this stream after the first.</summary>
    public void Evolve(object @event)
    {
        switch (@event)
        {
            case AccountOpened e:
                When(e);
                break;
            case DuesAssessed e:
                When(e);
                break;
            case PaymentAttested e:
                When(e);
                break;
            case PaymentConfirmed e:
                When(e);
                break;
            case PaymentRejected e:
                When(e);
                break;
            default:
                throw new InvalidOperationException($"MemberAccount does not know the event {@event.GetType().Name}.");
        }
    }

    /// <summary>Rebuilds an account from its history, starting with the event that opened it.</summary>
    public static MemberAccount Replay(AccountOpened opened, params object[] history)
    {
        var account = new MemberAccount();
        account.When(opened);
        foreach (var @event in history)
        {
            account.Evolve(@event);
        }

        return account;
    }

    private Attestation RequireAttestation(Guid attestationId) =>
        _attestations.TryGetValue(attestationId, out var attestation)
            ? attestation
            : throw new DomainException("There is no such payment attestation on this account.");

    private static void RequirePositive(decimal amount, string what)
    {
        if (amount <= 0)
        {
            throw new DomainException($"{what} must be a positive amount.");
        }
    }
}

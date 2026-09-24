using BallBank.Domain.Treasury;

namespace BallBank.Domain.Tests.Treasury;

public class MemberAccountTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly DueDate = new(2026, 10, 1);
    private static readonly Guid Treasurer = Guid.NewGuid();
    private static readonly Guid Member = Guid.NewGuid();

    private static AccountOpened Opened() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "2026", Member, Now);

    private static DuesAssessed Assessed(decimal amount, Guid? assessmentId = null) =>
        new(assessmentId ?? Guid.NewGuid(), amount, DueDate, "Season dues", Treasurer, Now);

    private static PaymentAttested Attested(decimal amount, Guid? attestationId = null, PaymentRail rail = PaymentRail.Venmo) =>
        new(attestationId ?? Guid.NewGuid(), amount, rail, "VN-1234", Member, Now);

    private static AssessDues AssessCommand(MemberAccount account, decimal amount, Guid? assessmentId = null) =>
        new(account.Id, assessmentId ?? Guid.NewGuid(), amount, DueDate, "Season dues", Treasurer);

    [Fact]
    public void A_new_account_owes_nothing()
    {
        var account = MemberAccount.Replay(Opened());

        account.Balance.ShouldBe(0m);
        account.Assessed.ShouldBe(0m);
        account.Confirmed.ShouldBe(0m);
        account.Attestations.ShouldBeEmpty();
    }

    [Fact]
    public void Opening_an_account_needs_a_season()
    {
        Should.Throw<DomainException>(() =>
        {
            MemberAccount.Open(new OpenAccount(Guid.NewGuid(), Guid.NewGuid(), " ", Member), Now);
        });
    }

    [Fact]
    public void Assessing_dues_raises_the_balance()
    {
        var account = MemberAccount.Replay(Opened());

        var assessed = account.Assess(AssessCommand(account, 50m), Now);

        assessed.ShouldNotBeNull();
        assessed!.Amount.ShouldBe(50m);
        account.Evolve(assessed);
        account.Balance.ShouldBe(50m);
    }

    [Fact]
    public void Assessing_the_same_assessment_twice_is_a_no_op()
    {
        var assessmentId = Guid.NewGuid();
        var account = MemberAccount.Replay(Opened(), Assessed(50m, assessmentId));

        var replay = account.Assess(AssessCommand(account, 50m, assessmentId), Now);

        replay.ShouldBeNull();
        account.Balance.ShouldBe(50m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-25)]
    public void Dues_must_be_a_positive_amount(int amount)
    {
        var account = MemberAccount.Replay(Opened());

        Should.Throw<DomainException>(() =>
        {
            account.Assess(AssessCommand(account, amount), Now);
        });
    }

    [Fact]
    public void A_venmo_payment_needs_a_reference()
    {
        var account = MemberAccount.Replay(Opened(), Assessed(50m));

        Should.Throw<DomainException>(() =>
        {
            account.Attest(new AttestPayment(account.Id, Guid.NewGuid(), 50m, PaymentRail.Venmo, "  ", Member), Now);
        });
    }

    [Fact]
    public void Cash_needs_no_reference()
    {
        var account = MemberAccount.Replay(Opened(), Assessed(50m));

        var attested = account.Attest(new AttestPayment(account.Id, Guid.NewGuid(), 50m, PaymentRail.Cash, null, Member), Now);

        attested.ShouldNotBeNull();
        attested!.Rail.ShouldBe(PaymentRail.Cash);
    }

    [Fact]
    public void An_attested_payment_does_not_count_until_confirmed()
    {
        var account = MemberAccount.Replay(Opened(), Assessed(50m), Attested(50m));

        account.Balance.ShouldBe(50m);
        account.PendingAttestations.Count().ShouldBe(1);
    }

    [Fact]
    public void Confirming_a_payment_lowers_the_balance()
    {
        var attestationId = Guid.NewGuid();
        var account = MemberAccount.Replay(Opened(), Assessed(50m), Attested(50m, attestationId));

        var confirmed = account.Confirm(new ConfirmPayment(account.Id, attestationId, Treasurer), Now);

        confirmed.ShouldNotBeNull();
        account.Evolve(confirmed!);
        account.Balance.ShouldBe(0m);
        account.Confirmed.ShouldBe(50m);
        account.PendingAttestations.ShouldBeEmpty();
    }

    [Fact]
    public void Confirming_twice_is_a_no_op()
    {
        var attestationId = Guid.NewGuid();
        var account = MemberAccount.Replay(
            Opened(),
            Assessed(50m),
            Attested(50m, attestationId),
            new PaymentConfirmed(attestationId, Treasurer, Now));

        var replay = account.Confirm(new ConfirmPayment(account.Id, attestationId, Treasurer), Now);

        replay.ShouldBeNull();
        account.Confirmed.ShouldBe(50m);
    }

    [Fact]
    public void An_unknown_attestation_cannot_be_confirmed()
    {
        var account = MemberAccount.Replay(Opened(), Assessed(50m));

        Should.Throw<DomainException>(() =>
        {
            account.Confirm(new ConfirmPayment(account.Id, Guid.NewGuid(), Treasurer), Now);
        });
    }

    [Fact]
    public void A_rejected_attestation_cannot_be_confirmed()
    {
        var attestationId = Guid.NewGuid();
        var account = MemberAccount.Replay(
            Opened(),
            Assessed(50m),
            Attested(50m, attestationId),
            new PaymentRejected(attestationId, Treasurer, "Nothing arrived", Now));

        Should.Throw<DomainException>(() =>
        {
            account.Confirm(new ConfirmPayment(account.Id, attestationId, Treasurer), Now);
        });
    }

    [Fact]
    public void A_confirmed_payment_cannot_be_rejected()
    {
        var attestationId = Guid.NewGuid();
        var account = MemberAccount.Replay(
            Opened(),
            Assessed(50m),
            Attested(50m, attestationId),
            new PaymentConfirmed(attestationId, Treasurer, Now));

        Should.Throw<DomainException>(() =>
        {
            account.Reject(new RejectPayment(account.Id, attestationId, Treasurer, "Changed my mind"), Now);
        });
    }

    [Fact]
    public void Rejecting_a_payment_needs_a_reason()
    {
        var attestationId = Guid.NewGuid();
        var account = MemberAccount.Replay(Opened(), Assessed(50m), Attested(50m, attestationId));

        Should.Throw<DomainException>(() =>
        {
            account.Reject(new RejectPayment(account.Id, attestationId, Treasurer, ""), Now);
        });
    }

    [Fact]
    public void Rejecting_leaves_the_balance_owed()
    {
        var attestationId = Guid.NewGuid();
        var account = MemberAccount.Replay(Opened(), Assessed(50m), Attested(50m, attestationId));

        var rejected = account.Reject(new RejectPayment(account.Id, attestationId, Treasurer, "Wrong amount"), Now);

        rejected.ShouldNotBeNull();
        account.Evolve(rejected!);
        account.Balance.ShouldBe(50m);
        account.PendingAttestations.ShouldBeEmpty();
    }

    [Fact]
    public void The_balance_survives_a_messy_history()
    {
        var dues = Guid.NewGuid();
        var lateFee = Guid.NewGuid();
        var firstTry = Guid.NewGuid();
        var wrongAmount = Guid.NewGuid();
        var secondTry = Guid.NewGuid();

        var account = MemberAccount.Replay(
            Opened(),
            Assessed(50m, dues),
            Assessed(10m, lateFee),
            Attested(50m, firstTry),
            new PaymentConfirmed(firstTry, Treasurer, Now),
            Attested(5m, wrongAmount),
            new PaymentRejected(wrongAmount, Treasurer, "Late fee is $10", Now),
            Attested(10m, secondTry),
            new PaymentConfirmed(secondTry, Treasurer, Now));

        account.Assessed.ShouldBe(60m);
        account.Confirmed.ShouldBe(60m);
        account.Balance.ShouldBe(0m);
        account.Attestations.Count.ShouldBe(3);
        account.PendingAttestations.ShouldBeEmpty();
    }
}

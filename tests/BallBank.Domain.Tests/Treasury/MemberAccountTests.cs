using System.Globalization;
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

    private static PostAdjustment AdjustCommand(MemberAccount account, decimal amount, string reason = "Waived: hosted the draft", Guid? adjustmentId = null, bool refund = false) =>
        new(account.Id, adjustmentId ?? Guid.NewGuid(), amount, reason, refund, Treasurer);

    private static AdjustmentPosted Adjusted(decimal amount, Guid? adjustmentId = null, bool refund = false) =>
        new(adjustmentId ?? Guid.NewGuid(), amount, refund ? "Refunded" : "Correction", refund, Treasurer, Now);

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

    [Theory]
    [InlineData("50.001")]
    [InlineData("0.005")]
    public void An_assessment_is_in_whole_cents(string amount)
    {
        var account = MemberAccount.Replay(Opened());

        Should.Throw<DomainException>(() =>
        {
            account.Assess(AssessCommand(account, decimal.Parse(amount, CultureInfo.InvariantCulture)), Now);
        }).Message.ShouldBe("An assessment must be in whole cents.");
    }

    [Fact]
    public void An_assessment_to_the_cent_is_recorded()
    {
        var account = MemberAccount.Replay(Opened());

        account.Assess(AssessCommand(account, 12.50m), Now).ShouldNotBeNull().Amount.ShouldBe(12.50m);
    }

    [Fact]
    public void An_assessment_needs_an_id()
    {
        var account = MemberAccount.Replay(Opened());

        Should.Throw<DomainException>(() =>
        {
            account.Assess(AssessCommand(account, 10m, Guid.Empty), Now);
        }).Message.ShouldBe("An assessment needs an id.");
    }

    [Fact]
    public void An_assessment_needs_a_due_date()
    {
        var account = MemberAccount.Replay(Opened());

        Should.Throw<DomainException>(() =>
        {
            account.Assess(AssessCommand(account, 10m) with { DueDate = null }, Now);
        }).Message.ShouldBe("An assessment needs a due date.");
    }

    [Theory]
    [InlineData("50.001")]
    [InlineData("0.005")]
    public void A_payment_is_in_whole_cents(string amount)
    {
        var account = MemberAccount.Replay(Opened(), Assessed(50m));

        Should.Throw<DomainException>(() =>
        {
            account.Attest(new AttestPayment(account.Id, Guid.NewGuid(), decimal.Parse(amount, CultureInfo.InvariantCulture), PaymentRail.Cash, null, Member), Now);
        }).Message.ShouldBe("A payment must be in whole cents.");
    }

    [Fact]
    public void Trailing_zeros_are_still_whole_cents()
    {
        var account = MemberAccount.Replay(Opened(), Assessed(50m));

        var attested = account.Attest(new AttestPayment(account.Id, Guid.NewGuid(), 12.500m, PaymentRail.Cash, null, Member), Now);

        attested.ShouldNotBeNull().Amount.ShouldBe(12.5m);
    }

    [Fact]
    public void A_partial_payment_once_confirmed_leaves_the_rest_owed()
    {
        var attestationId = Guid.NewGuid();
        var account = MemberAccount.Replay(Opened(), Assessed(50m), Attested(20m, attestationId));

        account.Evolve(account.Confirm(new ConfirmPayment(account.Id, attestationId, Treasurer), Now)!);

        account.Balance.ShouldBe(30m);
    }

    [Fact]
    public void An_overpayment_once_confirmed_leaves_the_pot_owing_the_member()
    {
        var attestationId = Guid.NewGuid();
        var account = MemberAccount.Replay(Opened(), Assessed(50m), Attested(60m, attestationId));

        account.Evolve(account.Confirm(new ConfirmPayment(account.Id, attestationId, Treasurer), Now)!);

        account.Balance.ShouldBe(-10m);
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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_adjustment_needs_a_reason(string reason)
    {
        var account = MemberAccount.Replay(Opened(), Assessed(50m));

        Should.Throw<DomainException>(() =>
        {
            account.PostAdjustment(AdjustCommand(account, -50m, reason), Now);
        }).Message.ShouldBe("An adjustment needs a reason the member will see.");
    }

    [Fact]
    public void An_adjustment_of_nothing_is_refused()
    {
        var account = MemberAccount.Replay(Opened(), Assessed(50m));

        Should.Throw<DomainException>(() =>
        {
            account.PostAdjustment(AdjustCommand(account, 0m), Now);
        }).Message.ShouldBe("An adjustment must raise or lower the balance.");
    }

    [Theory]
    [InlineData("10.001")]
    [InlineData("-0.005")]
    public void An_adjustment_is_in_whole_cents(string amount)
    {
        var account = MemberAccount.Replay(Opened(), Assessed(50m));

        Should.Throw<DomainException>(() =>
        {
            account.PostAdjustment(AdjustCommand(account, decimal.Parse(amount, CultureInfo.InvariantCulture)), Now);
        }).Message.ShouldBe("An adjustment must be in whole cents.");
    }

    [Fact]
    public void An_adjustment_needs_an_id()
    {
        var account = MemberAccount.Replay(Opened(), Assessed(50m));

        Should.Throw<DomainException>(() =>
        {
            account.PostAdjustment(AdjustCommand(account, -10m, adjustmentId: Guid.Empty), Now);
        }).Message.ShouldBe("An adjustment needs an id.");
    }

    [Fact]
    public void A_negative_adjustment_lowers_the_balance()
    {
        var account = MemberAccount.Replay(Opened(), Assessed(50m));

        var posted = account.PostAdjustment(AdjustCommand(account, -50m, " Waived: hosted the draft "), Now);

        posted.ShouldNotBeNull().ShouldSatisfyAllConditions(
            p => p.Amount.ShouldBe(-50m),
            p => p.Reason.ShouldBe("Waived: hosted the draft"),
            p => p.PostedBy.ShouldBe(Treasurer),
            p => p.PostedAt.ShouldBe(Now));
        account.Evolve(posted);
        account.Adjusted.ShouldBe(-50m);
        account.Balance.ShouldBe(0m);
    }

    [Fact]
    public void A_positive_adjustment_raises_the_balance()
    {
        var account = MemberAccount.Replay(Opened(), Assessed(50m));

        account.Evolve(account.PostAdjustment(AdjustCommand(account, 12.50m, "Missed the dues deadline"), Now)!);

        account.Balance.ShouldBe(62.50m);
        account.Assessed.ShouldBe(50m);
    }

    [Fact]
    public void Posting_the_same_adjustment_twice_is_a_no_op()
    {
        var adjustmentId = Guid.NewGuid();
        var account = MemberAccount.Replay(Opened(), Assessed(50m), Adjusted(-50m, adjustmentId));

        var replay = account.PostAdjustment(AdjustCommand(account, -50m, adjustmentId: adjustmentId), Now);

        replay.ShouldBeNull();
        account.Balance.ShouldBe(0m);
    }

    [Fact]
    public void The_balance_survives_a_messy_history_with_adjustments_and_an_overpayment()
    {
        var overpaid = Guid.NewGuid();
        var bounced = Guid.NewGuid();
        var sideBet = Guid.NewGuid();

        // $60 owed less a $10 waiver; $70 paid and the $20 overpaid refunded; then $20 more paid against a
        // $10 correction, so the pot owes the member $10.
        var account = MemberAccount.Replay(
            Opened(),
            Assessed(50m),
            Assessed(10m),
            Adjusted(-10m),
            Attested(70m, overpaid),
            new PaymentConfirmed(overpaid, Treasurer, Now),
            Attested(20m, bounced),
            new PaymentRejected(bounced, Treasurer, "Nothing arrived", Now),
            Adjusted(20m, refund: true),
            Attested(20m, sideBet),
            new PaymentConfirmed(sideBet, Treasurer, Now),
            Adjusted(10m));

        account.Assessed.ShouldBe(60m);
        account.Confirmed.ShouldBe(90m);
        account.Adjusted.ShouldBe(20m);
        account.Balance.ShouldBe(-10m);
        account.Refunded.ShouldBe(20m);
        account.InThePot.ShouldBe(70m);
        account.PendingAttestations.ShouldBeEmpty();
    }

    [Fact]
    public void A_refund_of_an_overpayment_settles_the_balance_and_leaves_the_pot()
    {
        var attestationId = Guid.NewGuid();
        var account = MemberAccount.Replay(
            Opened(), Assessed(50m), Attested(60m, attestationId), new PaymentConfirmed(attestationId, Treasurer, Now));

        var refunded = account.PostAdjustment(AdjustCommand(account, 10m, "Refunded the $10 overpaid", refund: true), Now);

        refunded.ShouldNotBeNull().Refund.ShouldBeTrue();
        account.Evolve(refunded);
        account.Balance.ShouldBe(0m);
        account.Confirmed.ShouldBe(60m);
        account.Refunded.ShouldBe(10m);
        account.InThePot.ShouldBe(50m);
    }

    [Fact]
    public void A_waiver_leaves_the_pot_alone()
    {
        var account = MemberAccount.Replay(Opened(), Assessed(50m));

        account.Evolve(account.PostAdjustment(AdjustCommand(account, -50m), Now)!);

        account.Refunded.ShouldBe(0m);
        account.InThePot.ShouldBe(0m);
    }

    [Fact]
    public void A_refund_pays_the_member_back_so_it_raises_the_balance()
    {
        var account = MemberAccount.Replay(Opened(), Assessed(50m));

        Should.Throw<DomainException>(() =>
        {
            account.PostAdjustment(AdjustCommand(account, -10m, "Refunded", refund: true), Now);
        }).Message.ShouldBe("A refund pays the member back, so it must raise the balance.");
    }

    [Theory]
    [InlineData("10.01")]
    [InlineData("50")]
    public void A_refund_cannot_be_more_than_the_pot_owes_the_member(string amount)
    {
        var attestationId = Guid.NewGuid();
        var account = MemberAccount.Replay(
            Opened(), Assessed(50m), Attested(60m, attestationId), new PaymentConfirmed(attestationId, Treasurer, Now));

        Should.Throw<DomainException>(() =>
        {
            account.PostAdjustment(AdjustCommand(account, decimal.Parse(amount, CultureInfo.InvariantCulture), "Refunded", refund: true), Now);
        }).Message.ShouldBe("A refund cannot be more than the pot owes the member ($10.00).");
    }

    [Fact]
    public void A_member_the_pot_owes_nothing_cannot_be_refunded()
    {
        var account = MemberAccount.Replay(Opened(), Assessed(50m));

        Should.Throw<DomainException>(() =>
        {
            account.PostAdjustment(AdjustCommand(account, 10m, "Refunded", refund: true), Now);
        }).Message.ShouldBe("A refund cannot be more than the pot owes the member ($0.00).");
    }

    [Fact]
    public void A_refund_can_be_part_of_what_the_pot_owes()
    {
        var attestationId = Guid.NewGuid();
        var account = MemberAccount.Replay(
            Opened(), Assessed(50m), Attested(60m, attestationId), new PaymentConfirmed(attestationId, Treasurer, Now));

        account.Evolve(account.PostAdjustment(AdjustCommand(account, 4m, "Refunded part", refund: true), Now)!);

        account.Balance.ShouldBe(-6m);
    }
}

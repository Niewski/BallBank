using BallBank.Domain.Notifications;
using BallBank.Domain.Treasury;

namespace BallBank.Domain.Tests.Notifications;

public class SmsTextsTests
{
    private const string League = "Holland Hogs";
    private const string Link = "https://ballbank.example/statement?league=L&account=A";

    [Fact]
    public void The_statement_link_names_the_league_and_the_account()
    {
        var league = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var account = Guid.Parse("22222222-2222-2222-2222-222222222222");

        SmsTexts.StatementLink("https://ballbank.example", league, account)
            .ShouldBe("https://ballbank.example/statement?league=11111111-1111-1111-1111-111111111111&account=22222222-2222-2222-2222-222222222222");
    }

    [Fact]
    public void The_statement_link_does_not_double_the_slash_after_the_site()
    {
        SmsTexts.StatementLink("https://ballbank.example/", Guid.Empty, Guid.Empty)
            .ShouldStartWith("https://ballbank.example/statement?");
    }

    [Fact]
    public void Assessed_dues_tell_the_member_the_amount_the_memo_and_when_they_are_due()
    {
        SmsTexts.DuesAssessed(League, 25m, "Trophy fund", new DateOnly(2026, 11, 1), Link)
            .ShouldBe($"BallBank: Holland Hogs assessed you $25.00 for Trophy fund, due Nov 1, 2026. Your statement: {Link}");
    }

    [Fact]
    public void Assessed_dues_with_no_memo_do_not_say_for_nothing()
    {
        SmsTexts.DuesAssessed(League, 50m, "", new DateOnly(2026, 10, 1), Link)
            .ShouldBe($"BallBank: Holland Hogs assessed you $50.00, due Oct 1, 2026. Your statement: {Link}");
    }

    [Fact]
    public void An_attestation_tells_the_treasurer_who_paid_how_much_by_what_and_the_reference()
    {
        SmsTexts.PaymentAttested(League, "Sam", 50m, PaymentRail.Venmo, "VN-1234", Link)
            .ShouldBe($"BallBank: Sam says they paid $50.00 by Venmo (VN-1234) in Holland Hogs. Confirm or reject it: {Link}");
    }

    [Fact]
    public void An_attestation_with_no_reference_does_not_name_one()
    {
        SmsTexts.PaymentAttested(League, "Jacob", 30m, PaymentRail.Cash, null, Link)
            .ShouldBe($"BallBank: Jacob says they paid $30.00 by Cash in Holland Hogs. Confirm or reject it: {Link}");
    }

    [Fact]
    public void A_confirmation_tells_the_member_which_payment_was_confirmed()
    {
        SmsTexts.PaymentConfirmed(League, 50m, PaymentRail.Venmo, Link)
            .ShouldBe($"BallBank: Holland Hogs confirmed your $50.00 Venmo payment. Your statement: {Link}");
    }

    [Fact]
    public void A_rejection_carries_its_reason()
    {
        SmsTexts.PaymentRejected(League, 20m, PaymentRail.Zelle, "Nothing arrived", Link)
            .ShouldBe($"BallBank: Holland Hogs rejected your $20.00 Zelle payment: Nothing arrived. Your statement: {Link}");
    }

    [Fact]
    public void A_waiver_tells_the_member_their_balance_went_down_and_why()
    {
        SmsTexts.AdjustmentPosted(League, -5m, "Waived: hosted the draft", refund: false, Link)
            .ShouldBe($"BallBank: Holland Hogs lowered your balance by $5.00: Waived: hosted the draft. Your statement: {Link}");
    }

    [Fact]
    public void A_charge_tells_the_member_their_balance_went_up_and_why()
    {
        SmsTexts.AdjustmentPosted(League, 10m, "Late fee", refund: false, Link)
            .ShouldBe($"BallBank: Holland Hogs raised your balance by $10.00: Late fee. Your statement: {Link}");
    }

    [Fact]
    public void A_refund_tells_the_member_they_were_paid_back_out_of_the_pot()
    {
        SmsTexts.AdjustmentPosted(League, 10m, "Refund of the overpayment", refund: true, Link)
            .ShouldBe($"BallBank: Holland Hogs refunded you $10.00 out of the pot: Refund of the overpayment. Your statement: {Link}");
    }

    private static readonly DateOnly DueDate = new(2026, 10, 1);

    [Fact]
    public void A_reminder_before_the_due_date_says_how_many_days_are_left()
    {
        SmsTexts.Reminder(League, 25m, DueDate, today: DueDate.AddDays(-3), Link)
            .ShouldBe($"BallBank: You owe Holland Hogs $25.00, due in 3 days on Oct 1, 2026. Your statement: {Link}");
    }

    [Fact]
    public void A_reminder_the_day_before_says_tomorrow()
    {
        SmsTexts.Reminder(League, 25m, DueDate, today: DueDate.AddDays(-1), Link)
            .ShouldBe($"BallBank: You owe Holland Hogs $25.00, due tomorrow, Oct 1, 2026. Your statement: {Link}");
    }

    [Fact]
    public void A_reminder_on_the_due_date_says_today()
    {
        SmsTexts.Reminder(League, 25m, DueDate, today: DueDate, Link)
            .ShouldBe($"BallBank: You owe Holland Hogs $25.00, due today. Your statement: {Link}");
    }

    [Fact]
    public void A_reminder_after_the_due_date_says_how_overdue_it_is()
    {
        SmsTexts.Reminder(League, 25m, DueDate, today: DueDate.AddDays(7), Link)
            .ShouldBe($"BallBank: You owe Holland Hogs $25.00, 7 days overdue since Oct 1, 2026. Your statement: {Link}");
    }

    [Fact]
    public void A_reminder_one_day_overdue_says_day_not_days()
    {
        SmsTexts.Reminder(League, 25m, DueDate, today: DueDate.AddDays(1), Link)
            .ShouldBe($"BallBank: You owe Holland Hogs $25.00, 1 day overdue since Oct 1, 2026. Your statement: {Link}");
    }

    [Fact]
    public void Every_text_names_BallBank_and_the_league_and_carries_the_link()
    {
        string[] texts =
        [
            SmsTexts.DuesAssessed(League, 25m, "Trophy fund", new DateOnly(2026, 11, 1), Link),
            SmsTexts.PaymentAttested(League, "Sam", 50m, PaymentRail.Venmo, "VN-1", Link),
            SmsTexts.PaymentConfirmed(League, 50m, PaymentRail.Venmo, Link),
            SmsTexts.PaymentRejected(League, 20m, PaymentRail.Zelle, "Nothing arrived", Link),
            SmsTexts.AdjustmentPosted(League, -5m, "Waived", refund: false, Link),
            SmsTexts.Reminder(League, 25m, DueDate, DueDate.AddDays(14), Link),
        ];

        foreach (var text in texts)
        {
            text.ShouldStartWith("BallBank: ");
            text.ShouldContain(League);
            text.ShouldEndWith(Link);
        }
    }
}

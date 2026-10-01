using BallBank.Domain.Notifications;

namespace BallBank.Domain.Tests.Notifications;

public class NotificationTextsTests
{
    [Fact]
    public void Hello_says_which_league_is_connected() =>
        NotificationTexts.Hello("Holland Hogs").ShouldBe("Hello from BallBank. Holland Hogs is connected to this channel.");

    [Fact]
    public void A_season_opening_announces_the_dues_and_when_they_are_due() =>
        NotificationTexts.SeasonOpened("Holland Hogs", "2026", 50m, new DateOnly(2026, 10, 1))
            .ShouldBe("Holland Hogs opened the 2026 season. Dues are $50.00, due Oct 1, 2026.");

    [Fact]
    public void A_confirmed_payment_names_the_team_the_amount_and_the_pot() =>
        NotificationTexts.PaymentConfirmed("Sam's Slammers", 50m, 250m)
            .ShouldBe("Sam's Slammers paid $50.00 and the treasurer confirmed it. The pot is now $250.00.");

    [Fact]
    public void Amounts_read_as_dollars_and_cents() =>
        NotificationTexts.PaymentConfirmed("Jacob's Jets", 12.5m, 1234.5m)
            .ShouldBe("Jacob's Jets paid $12.50 and the treasurer confirmed it. The pot is now $1234.50.");

    private static string Lines(params string[] lines) => string.Join("\n", lines);

    [Fact]
    public void A_digest_says_who_still_owes_the_pot_and_the_payments_waiting_on_the_treasurer() =>
        NotificationTexts.Digest(
                "Holland Hogs",
                "2026",
                [new MemberBalance("Sam's Slammers", 50m), new MemberBalance("Hog Wild", 25m)],
                pot: 100m,
                attestationsPending: 2)
            .ShouldBe(Lines(
                "Weekly digest for Holland Hogs, 2026 season.",
                "Still owing: Sam's Slammers $50.00, Hog Wild $25.00.",
                "The pot holds $100.00.",
                "2 payments are waiting for the treasurer to confirm."));

    [Fact]
    public void A_digest_lists_whoever_owes_most_first_and_equal_debts_by_team_name() =>
        NotificationTexts.Digest(
                "Holland Hogs",
                "2026",
                [
                    new MemberBalance("Waiver Wire", 25m),
                    new MemberBalance("Hog Wild", 50m),
                    new MemberBalance("Bye Week", 25m),
                    new MemberBalance("Sam's Slammers", 75.5m),
                ],
                pot: 0m,
                attestationsPending: 0)
            .ShouldContain("Still owing: Sam's Slammers $75.50, Hog Wild $50.00, Bye Week $25.00, Waiver Wire $25.00.");

    [Fact]
    public void A_digest_leaves_out_members_who_owe_nothing_or_are_owed() =>
        NotificationTexts.Digest(
                "Holland Hogs",
                "2026",
                [new MemberBalance("Paid Up", 0m), new MemberBalance("Sam's Slammers", 50m), new MemberBalance("Overpaid", -10m)],
                pot: 100m,
                attestationsPending: 0)
            .ShouldContain("Still owing: Sam's Slammers $50.00.");

    [Fact]
    public void A_digest_says_so_when_everyone_has_paid_up()
    {
        var digest = NotificationTexts.Digest(
            "Holland Hogs", "2026", [new MemberBalance("Hog Wild", 0m), new MemberBalance("Sam's Slammers", -5m)], pot: 400m, attestationsPending: 0);

        digest.ShouldBe(Lines(
            "Weekly digest for Holland Hogs, 2026 season.",
            "Everyone has paid up.",
            "The pot holds $400.00.",
            "No payments are waiting for the treasurer to confirm."));
    }

    [Fact]
    public void A_digest_says_so_when_there_are_no_members_at_all() =>
        NotificationTexts.Digest("Holland Hogs", "2026", [], pot: 0m, attestationsPending: 0)
            .ShouldBe(Lines(
                "Weekly digest for Holland Hogs, 2026 season.",
                "Everyone has paid up.",
                "The pot holds $0.00.",
                "No payments are waiting for the treasurer to confirm."));

    [Theory]
    [InlineData(0, "No payments are waiting for the treasurer to confirm.")]
    [InlineData(1, "1 payment is waiting for the treasurer to confirm.")]
    [InlineData(2, "2 payments are waiting for the treasurer to confirm.")]
    [InlineData(12, "12 payments are waiting for the treasurer to confirm.")]
    public void A_digest_counts_the_payments_waiting_on_the_treasurer(int waiting, string sentence) =>
        NotificationTexts.Digest("Holland Hogs", "2026", [new MemberBalance("Hog Wild", 10m)], pot: 10m, attestationsPending: waiting)
            .Split('\n')[^1]
            .ShouldBe(sentence);
}

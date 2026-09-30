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
}

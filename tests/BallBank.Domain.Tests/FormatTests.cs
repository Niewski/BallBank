namespace BallBank.Domain.Tests;

public class FormatTests
{
    [Theory]
    [InlineData(50, "$50.00")]
    [InlineData(10.5, "$10.50")]
    [InlineData(0, "$0.00")]
    [InlineData(1234.56, "$1234.56")]
    public void Money_reads_as_dollars_and_cents(decimal amount, string expected) =>
        Format.Money(amount).ShouldBe(expected);

    [Fact]
    public void Money_drops_the_sign_so_the_sentence_can_say_which_way_it_went() =>
        Format.Money(-20m).ShouldBe("$20.00");

    [Fact]
    public void A_date_reads_as_month_day_and_year() =>
        Format.Date(new DateOnly(2026, 10, 1)).ShouldBe("Oct 1, 2026");
}

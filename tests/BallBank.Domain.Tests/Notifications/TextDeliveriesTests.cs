using BallBank.Domain.Notifications;

namespace BallBank.Domain.Tests.Notifications;

public class TextDeliveriesTests
{
    [Theory]
    [InlineData("accepted")]
    [InlineData("scheduled")]
    [InlineData("queued")]
    [InlineData("sending")]
    [InlineData("sent")]
    public void A_text_the_carrier_has_not_settled_is_still_in_flight(string reported)
    {
        TextDeliveries.Read(reported).ShouldBe(TextDelivery.InFlight);
    }

    [Fact]
    public void A_delivered_text_is_delivered()
    {
        TextDeliveries.Read("delivered").ShouldBe(TextDelivery.Delivered);
    }

    [Fact]
    public void An_undelivered_text_is_undelivered()
    {
        TextDeliveries.Read("undelivered").ShouldBe(TextDelivery.Undelivered);
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("canceled")]
    public void A_text_that_never_left_Twilio_failed(string reported)
    {
        TextDeliveries.Read(reported).ShouldBe(TextDelivery.Failed);
    }

    [Theory]
    [InlineData("DELIVERED", TextDelivery.Delivered)]
    [InlineData(" Failed ", TextDelivery.Failed)]
    public void The_status_is_read_whatever_its_case(string reported, TextDelivery expected)
    {
        TextDeliveries.Read(reported).ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("received")]
    [InlineData("read")]
    [InlineData("rocketed")]
    public void A_status_that_says_nothing_about_an_outgoing_text_is_not_read(string? reported)
    {
        TextDeliveries.Read(reported).ShouldBeNull();
    }
}

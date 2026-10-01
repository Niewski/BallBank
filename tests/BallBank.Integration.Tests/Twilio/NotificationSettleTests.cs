using BallBank.Api.Features.Notifications;
using BallBank.Domain.Notifications;

namespace BallBank.Integration.Tests.Twilio;

public class NotificationSettleTests
{
    private static Notification Text(string status) => new() { Id = "sms/member/kind/cause", Status = status };

    [Theory]
    [InlineData(TextDelivery.Delivered, NotificationStatus.Delivered)]
    [InlineData(TextDelivery.Undelivered, NotificationStatus.Undelivered)]
    [InlineData(TextDelivery.Failed, NotificationStatus.Failed)]
    public void A_sent_text_settles_as_Twilio_reports_it(TextDelivery delivery, string expected)
    {
        var notification = Text(NotificationStatus.Sent);

        notification.Settle(delivery, null).ShouldBeTrue();

        notification.Status.ShouldBe(expected);
    }

    [Fact]
    public void A_text_still_in_flight_stays_sent()
    {
        var notification = Text(NotificationStatus.Sent);

        notification.Settle(TextDelivery.InFlight, null).ShouldBeFalse();

        notification.Status.ShouldBe(NotificationStatus.Sent);
    }

    [Theory]
    [InlineData(NotificationStatus.Delivered)]
    [InlineData(NotificationStatus.Undelivered)]
    [InlineData(NotificationStatus.Failed)]
    public void A_settled_text_is_never_changed(string settled)
    {
        foreach (var later in new[] { TextDelivery.InFlight, TextDelivery.Delivered, TextDelivery.Undelivered, TextDelivery.Failed })
        {
            var notification = Text(settled);

            notification.Settle(later, "30003").ShouldBeFalse();

            notification.Status.ShouldBe(settled);
            notification.Reason.ShouldBeNull();
        }
    }

    [Theory]
    [InlineData(NotificationStatus.Pending)]
    [InlineData(NotificationStatus.Skipped)]
    [InlineData(NotificationStatus.Held)]
    [InlineData(NotificationStatus.Dropped)]
    public void A_text_that_was_not_sent_is_not_settled(string status)
    {
        var notification = Text(status);

        notification.Settle(TextDelivery.Delivered, null).ShouldBeFalse();

        notification.Status.ShouldBe(status);
    }

    [Fact]
    public void A_failure_keeps_the_error_code_Twilio_gave()
    {
        var notification = Text(NotificationStatus.Sent);

        notification.Settle(TextDelivery.Undelivered, "30003");

        notification.Reason.ShouldBe("Twilio error 30003");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("30003; drop table")]
    [InlineData("123456789")]
    public void An_error_code_that_is_not_a_short_number_is_not_kept(string? errorCode)
    {
        var notification = Text(NotificationStatus.Sent);

        notification.Settle(TextDelivery.Failed, errorCode);

        notification.Reason.ShouldBeNull();
    }

    [Fact]
    public void A_delivered_text_keeps_no_error_code()
    {
        var notification = Text(NotificationStatus.Sent);

        notification.Settle(TextDelivery.Delivered, "30003");

        notification.Reason.ShouldBeNull();
    }
}

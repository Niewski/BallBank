using System.Net;
using System.Text;
using System.Web;
using BallBank.Api.Features.Notifications;
using BallBank.Api.Integrations.Twilio;
using Microsoft.Extensions.Options;

namespace BallBank.Integration.Tests.Twilio;

/// <summary>The Twilio channel on its own, over a handler that answers as it is told. No database.</summary>
public class TwilioSmsChannelTests
{
    private static readonly Guid League = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string Notification = "Sms/22222222-2222-2222-2222-222222222222/DuesAssessed/33333333-3333-3333-3333-333333333333";
    private const string Number = "+15550100002";

    private static readonly TwilioOptions Configured = new()
    {
        AccountSid = "ACtest0000000000000000000000000001",
        AuthToken = "test-auth-token-not-a-secret",
        FromNumber = "+15550100100",
        StatusCallbackBaseUrl = "https://ballbank.invalid/",
    };

    private static OutgoingNotification Message => new(League, Notification, Number, "BallBank: hello");

    [Fact]
    public async Task A_text_is_a_form_post_to_the_messages_resource_with_basic_credentials()
    {
        var twilio = new StubTwilio(HttpStatusCode.Created);

        await Channel(twilio, Configured).SendAsync(Message, CancellationToken.None);

        var request = twilio.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri.AbsolutePath.ShouldBe($"/2010-04-01/Accounts/{Configured.AccountSid}/Messages.json");
        request.Credentials.ShouldBe($"{Configured.AccountSid}:{Configured.AuthToken}");
        request.Form["To"].ShouldBe(Number);
        request.Form["From"].ShouldBe(Configured.FromNumber);
        request.Form["Body"].ShouldBe("BallBank: hello");
    }

    [Fact]
    public async Task The_status_callback_names_the_league_and_the_escaped_notification()
    {
        var twilio = new StubTwilio(HttpStatusCode.Created);

        await Channel(twilio, Configured).SendAsync(Message, CancellationToken.None);

        var callback = twilio.Requests.Single().Form["StatusCallback"]!;
        callback.ShouldBe($"https://ballbank.invalid/webhooks/twilio/status?league={League}&notification={Uri.EscapeDataString(Notification)}");
        callback.ShouldContain("%2F");
        var query = HttpUtility.ParseQueryString(new Uri(callback).Query);
        query["league"].ShouldBe(League.ToString());
        query["notification"].ShouldBe(Notification);
    }

    [Theory]
    [InlineData(null, "token", "+15550100100", "https://ballbank.invalid")]
    [InlineData("ACsid", null, "+15550100100", "https://ballbank.invalid")]
    [InlineData("ACsid", "token", "", "https://ballbank.invalid")]
    [InlineData("ACsid", "token", "+15550100100", " ")]
    public async Task Without_every_setting_nothing_is_sent_and_the_send_fails(string? sid, string? token, string? from, string? callbackBase)
    {
        var twilio = new StubTwilio(HttpStatusCode.Created);
        var options = new TwilioOptions { AccountSid = sid, AuthToken = token, FromNumber = from, StatusCallbackBaseUrl = callbackBase };

        await Should.ThrowAsync<NotificationDeliveryException>(() => Channel(twilio, options).SendAsync(Message, CancellationToken.None));

        twilio.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_refusal_fails_the_send_without_the_token_or_the_number_in_what_it_says(HttpStatusCode status)
    {
        var twilio = new StubTwilio(status, body: "{\"message\":\"To " + Number + " failed\"}");

        var failure = await Should.ThrowAsync<NotificationDeliveryException>(() => Channel(twilio, Configured).SendAsync(Message, CancellationToken.None));

        failure.Message.ShouldContain(((int)status).ToString());
        failure.Message.ShouldNotContain(Number);
        failure.Message.ShouldNotContain(Configured.AuthToken!);
        failure.Message.ShouldNotContain(Configured.AccountSid!);
    }

    [Fact]
    public async Task A_send_that_cannot_reach_Twilio_fails_without_the_token_or_the_number_in_what_it_says()
    {
        var twilio = new StubTwilio(failure: new HttpRequestException($"Connection to {Number} refused with {Configured.AuthToken}"));

        var failure = await Should.ThrowAsync<NotificationDeliveryException>(() => Channel(twilio, Configured).SendAsync(Message, CancellationToken.None));

        failure.InnerException.ShouldBeOfType<HttpRequestException>();
        failure.Message.ShouldNotContain(Number);
        failure.Message.ShouldNotContain(Configured.AuthToken!);
    }

    [Fact]
    public async Task A_send_that_is_cancelled_by_the_caller_is_not_a_delivery_failure()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var twilio = new StubTwilio(HttpStatusCode.Created);

        await Should.ThrowAsync<OperationCanceledException>(() => Channel(twilio, Configured).SendAsync(Message, cancelled.Token));
    }

    private static TwilioSmsChannel Channel(StubTwilio twilio, TwilioOptions options) =>
        new(new HttpClient(twilio) { BaseAddress = TwilioRegistration.BaseAddress }, Options.Create(options));

    private sealed class StubTwilio(HttpStatusCode status = HttpStatusCode.Created, string body = "{}", Exception? failure = null) : HttpMessageHandler
    {
        public List<Received> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (failure is not null)
            {
                throw failure;
            }

            var credentials = request.Headers.Authorization is { Scheme: "Basic", Parameter: { } parameter }
                ? Encoding.UTF8.GetString(Convert.FromBase64String(parameter))
                : null;
            Requests.Add(new Received(
                request.Method, request.RequestUri!, credentials, HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync(cancellation))));

            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed record Received(HttpMethod Method, Uri Uri, string? Credentials, System.Collections.Specialized.NameValueCollection Form);
}

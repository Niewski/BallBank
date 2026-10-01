using System.Net;
using System.Net.Http.Json;
using BallBank.Api.Features.Membership;
using BallBank.Api.Features.Notifications;
using BallBank.Domain.Notifications;
using BallBank.Integration.Tests.Twilio;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine.Tracking;

namespace BallBank.Integration.Tests.Http;

/// <summary>What Twilio tells the API: how a text it accepted got on, and what a member replied to one.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class TwilioWebhookTests(PostgresFixture postgres)
{
    // Mid-morning Eastern, outside the quiet hours a member has until they choose.
    private static readonly DateTimeOffset Monday = new(2026, 9, 28, 14, 0, 0, TimeSpan.Zero);

    private BallBankApi Api => postgres.Api;
    private FakeTwilio Twilio => Api.Twilio;
    private IDocumentStore Store => Api.Services.GetRequiredService<IDocumentStore>();

    [Fact]
    public async Task A_delivered_callback_records_that_the_text_was_delivered()
    {
        var sent = await ASentText("555 011 0001");

        var response = await ReportStatus(sent, "delivered");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Reload(sent)).Status.ShouldBe(NotificationStatus.Delivered);
    }

    [Fact]
    public async Task An_undelivered_callback_records_that_it_was_not_and_the_error_Twilio_gave()
    {
        var sent = await ASentText("555 011 0002");

        (await ReportStatus(sent, "undelivered", ("ErrorCode", "30003"))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var notification = await Reload(sent);
        notification.Status.ShouldBe(NotificationStatus.Undelivered);
        notification.Reason.ShouldBe("Twilio error 30003");
    }

    [Fact]
    public async Task A_failed_callback_records_that_the_text_failed()
    {
        var sent = await ASentText("555 011 0003");

        (await ReportStatus(sent, "failed", ("ErrorCode", "30007"))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var notification = await Reload(sent);
        notification.Status.ShouldBe(NotificationStatus.Failed);
        notification.Reason.ShouldBe("Twilio error 30007");
    }

    [Theory]
    [InlineData("queued")]
    [InlineData("sending")]
    [InlineData("sent")]
    public async Task A_text_still_on_its_way_stays_sent(string status)
    {
        var sent = await ASentText("555 011 0004");

        (await ReportStatus(sent, status)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await Reload(sent)).Status.ShouldBe(NotificationStatus.Sent);
    }

    [Fact]
    public async Task A_callback_delivered_twice_records_one_state()
    {
        var sent = await ASentText("555 011 0005");

        (await ReportStatus(sent, "delivered")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var once = await Reload(sent);
        (await ReportStatus(sent, "delivered")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var twice = await Reload(sent);
        twice.ShouldBeEquivalentTo(once);
        twice.Status.ShouldBe(NotificationStatus.Delivered);
    }

    [Fact]
    public async Task An_older_callback_arriving_late_does_not_move_a_delivered_text_back()
    {
        var sent = await ASentText("555 011 0006");
        await ReportStatus(sent, "delivered");

        (await ReportStatus(sent, "sent")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReportStatus(sent, "queued")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await Reload(sent)).Status.ShouldBe(NotificationStatus.Delivered);
    }

    [Fact]
    public async Task What_a_text_came_to_is_not_overwritten_by_another_ending()
    {
        var sent = await ASentText("555 011 0007");
        await ReportStatus(sent, "delivered");

        (await ReportStatus(sent, "undelivered", ("ErrorCode", "30003"))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var notification = await Reload(sent);
        notification.Status.ShouldBe(NotificationStatus.Delivered);
        notification.Reason.ShouldBeNull();
    }

    [Fact]
    public async Task A_status_that_says_nothing_about_an_outgoing_text_is_acknowledged_and_ignored()
    {
        var sent = await ASentText("555 011 0008");

        (await ReportStatus(sent, "received")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await Reload(sent)).Status.ShouldBe(NotificationStatus.Sent);
    }

    [Fact]
    public async Task A_callback_signed_with_another_token_is_refused_and_changes_nothing()
    {
        var sent = await ASentText("555 011 0009");
        var form = StatusForm("delivered");

        var response = await FakeTwilio.Post(Api.CreateClient(), sent.Url, form, Twilio.Sign(sent.Url, form, "not-the-token"));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Reload(sent)).Status.ShouldBe(NotificationStatus.Sent);
    }

    [Fact]
    public async Task An_unsigned_callback_is_refused_and_changes_nothing()
    {
        var sent = await ASentText("555 011 0010");

        var response = await FakeTwilio.Post(Api.CreateClient(), sent.Url, StatusForm("delivered"), signature: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Reload(sent)).Status.ShouldBe(NotificationStatus.Sent);
    }

    [Fact]
    public async Task A_callback_changed_after_it_was_signed_is_refused()
    {
        var sent = await ASentText("555 011 0011");
        var signature = Twilio.Sign(sent.Url, StatusForm("sent"));

        var response = await FakeTwilio.Post(Api.CreateClient(), sent.Url, StatusForm("delivered"), signature);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Reload(sent)).Status.ShouldBe(NotificationStatus.Sent);
    }

    [Fact]
    public async Task A_callback_signed_for_one_notification_cannot_be_replayed_against_another()
    {
        var first = await ASentText("555 011 0012");
        var second = await ASentText("555 011 0013");
        var form = StatusForm("delivered");

        var response = await FakeTwilio.Post(Api.CreateClient(), second.Url, form, Twilio.Sign(first.Url, form));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Reload(second)).Status.ShouldBe(NotificationStatus.Sent);
    }

    [Fact]
    public async Task A_signed_callback_for_a_league_that_does_not_hold_the_notification_finds_nothing()
    {
        var sent = await ASentText("555 011 0014");
        var elsewhere = $"{FakeTwilio.StatusCallbackBaseUrl}/webhooks/twilio/status?league={Guid.NewGuid()}&notification={Uri.EscapeDataString(sent.Notification.Id)}";

        (await Twilio.Report(Api.CreateClient(), elsewhere, StatusForm("delivered"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await Reload(sent)).Status.ShouldBe(NotificationStatus.Sent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?league=not-a-league&notification=sms")]
    [InlineData("?notification=sms")]
    [InlineData("?league=6e0c5a52-6b13-4d6d-a9d6-0d2f7d3d6c11")]
    [InlineData("?league=6e0c5a52-6b13-4d6d-a9d6-0d2f7d3d6c11&notification=sms%2Fnothing")]
    public async Task A_signed_callback_that_names_no_notification_finds_nothing(string query)
    {
        var url = $"{FakeTwilio.StatusCallbackBaseUrl}/webhooks/twilio/status{query}";

        (await Twilio.Report(Api.CreateClient(), url, StatusForm("delivered"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task STOP_records_that_the_number_opted_out()
    {
        var number = NewNumber();

        var response = await Twilio.Reply(Api.CreateClient(), number, "STOP");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await OptOutOf(number)).ShouldNotBeNull();
    }

    [Fact]
    public async Task STOP_is_read_whatever_its_case_or_spacing()
    {
        var number = NewNumber();

        (await Twilio.Reply(Api.CreateClient(), number, " stop ")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await OptOutOf(number)).ShouldNotBeNull();
    }

    [Fact]
    public async Task STOP_again_keeps_the_first_opt_out()
    {
        var number = NewNumber();
        using var _ = Api.Clock.Set(Monday);
        await Twilio.Reply(Api.CreateClient(), number, "STOP");
        var first = await OptOutOf(number);

        using var later = Api.Clock.Set(Monday.AddDays(3));
        (await Twilio.Reply(Api.CreateClient(), number, "STOP")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await OptOutOf(number)).ShouldNotBeNull().OptedOutAt.ShouldBe(first.ShouldNotBeNull().OptedOutAt);
        first.OptedOutAt.ShouldBe(Monday);
    }

    [Fact]
    public async Task START_removes_the_opt_out()
    {
        var number = NewNumber();
        await Twilio.Reply(Api.CreateClient(), number, "STOP");

        (await Twilio.Reply(Api.CreateClient(), number, "START")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await OptOutOf(number)).ShouldBeNull();
    }

    [Fact]
    public async Task START_from_a_number_that_never_stopped_changes_nothing()
    {
        var number = NewNumber();

        (await Twilio.Reply(Api.CreateClient(), number, "START")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await OptOutOf(number)).ShouldBeNull();
    }

    [Theory]
    [InlineData("Thanks!")]
    [InlineData("HELP")]
    [InlineData("please stop")]
    [InlineData("")]
    public async Task Any_other_reply_is_acknowledged_and_changes_nothing(string body)
    {
        var stopped = NewNumber();
        var never = NewNumber();
        await Twilio.Reply(Api.CreateClient(), stopped, "STOP");

        (await Twilio.Reply(Api.CreateClient(), stopped, body)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Twilio.Reply(Api.CreateClient(), never, body)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await OptOutOf(stopped)).ShouldNotBeNull();
        (await OptOutOf(never)).ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a number")]
    [InlineData("5550110000")]
    [InlineData("+1")]
    public async Task A_reply_from_something_that_is_not_a_number_is_acknowledged_and_changes_nothing(string from)
    {
        (await Twilio.Reply(Api.CreateClient(), from, "STOP")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var session = Store.QuerySession();
        (await session.Query<PhoneOptOut>().Where(optOut => optOut.Id == from).CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_forged_reply_is_refused_and_changes_nothing()
    {
        var number = NewNumber();
        var client = Api.CreateClient();
        (string, string)[] form = [("From", number), ("To", Twilio.FromNumber), ("Body", "STOP")];

        (await FakeTwilio.Post(client, FakeTwilio.InboundUrl, form, signature: null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await FakeTwilio.Post(client, FakeTwilio.InboundUrl, form, Twilio.Sign(FakeTwilio.InboundUrl, form, "not-the-token")))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await OptOutOf(number)).ShouldBeNull();
    }

    [Fact]
    public async Task A_forged_START_does_not_lift_an_opt_out()
    {
        var number = NewNumber();
        await Twilio.Reply(Api.CreateClient(), number, "STOP");

        var response = await FakeTwilio.Post(Api.CreateClient(), FakeTwilio.InboundUrl, [("From", number), ("Body", "START")], signature: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await OptOutOf(number)).ShouldNotBeNull();
    }

    [Fact]
    public async Task After_STOP_a_member_in_two_leagues_is_texted_in_neither_and_after_START_texts_resume()
    {
        const string Phone = "555 011 0020";
        const string Number = "+15550110020";
        using var _ = Api.Clock.Set(Monday);
        var hogs = await OpenSeason();
        var hawgs = await OpenSeason();
        await OptIn(hogs, Phone);
        await OptIn(hawgs, Phone);

        await Twilio.Reply(Api.CreateClient(), Number, "STOP");
        var stoppedInHogs = await AssessSam(hogs);
        var stoppedInHawgs = await AssessSam(hawgs);

        Twilio.Attempts(hogs.LeagueId).ShouldBe(0);
        Twilio.Attempts(hawgs.LeagueId).ShouldBe(0);
        (await NotificationFor(hogs, stoppedInHogs)).Status.ShouldBe(NotificationStatus.Skipped);
        (await NotificationFor(hawgs, stoppedInHawgs)).Reason.ShouldBe(SmsDelivery.OptedOut);
        (await ReadPreferences(hogs)).OptedOut.ShouldBeTrue();
        (await ReadPreferences(hawgs)).OptedOut.ShouldBeTrue();

        await Twilio.Reply(Api.CreateClient(), Number, "START");
        await AssessSam(hogs);
        await AssessSam(hawgs);

        (await Twilio.WaitForTexts(hogs.LeagueId, 1)).ShouldHaveSingleItem().To.ShouldBe(Number);
        (await Twilio.WaitForTexts(hawgs.LeagueId, 1)).ShouldHaveSingleItem().To.ShouldBe(Number);
        (await ReadPreferences(hogs)).OptedOut.ShouldBeFalse();
    }

    [Fact]
    public async Task After_START_a_member_who_never_consented_is_still_not_texted()
    {
        const string Number = "+15550110021";
        using var _ = Api.Clock.Set(Monday);
        var hogs = await OpenSeason();
        await RecordContact(hogs, "555 011 0021");

        await Twilio.Reply(Api.CreateClient(), Number, "STOP");
        await Twilio.Reply(Api.CreateClient(), Number, "START");
        var assessment = await AssessSam(hogs);

        (await NotificationFor(hogs, assessment)).Reason.ShouldBe(SmsDelivery.NoConsent);
        Twilio.Attempts(hogs.LeagueId).ShouldBe(0);
    }

    // A season in which Sam has opted in at the number, and had a text sent about an assessment.
    private async Task<SentStatusText> ASentText(string phone)
    {
        using var _ = Api.Clock.Set(Monday);
        var hogs = await OpenSeason();
        await OptIn(hogs, phone);
        var assessment = await AssessSam(hogs);

        var text = (await Twilio.WaitForTexts(hogs.LeagueId, 1)).Single();
        var notification = await NotificationFor(hogs, assessment);
        notification.Status.ShouldBe(NotificationStatus.Sent);
        return new SentStatusText(hogs, notification, text.StatusCallback.OriginalString);
    }

    private Task<HttpResponseMessage> ReportStatus(SentStatusText sent, string status, params (string, string)[] more) =>
        Twilio.Report(Api.CreateClient(), sent.Url, [.. StatusForm(status), .. more]);

    private static (string, string)[] StatusForm(string status) =>
        [("MessageSid", "SM0123456789abcdef0123456789abcdef"), ("MessageStatus", status), ("To", "+15550110000")];

    private async Task<Notification> Reload(SentStatusText sent)
    {
        await using var session = Store.QuerySession(sent.Hogs.LeagueId.ToString());
        return (await session.LoadAsync<Notification>(sent.Notification.Id)).ShouldNotBeNull();
    }

    private async Task<PhoneOptOut?> OptOutOf(string number)
    {
        await using var session = Store.QuerySession();
        return await session.LoadAsync<PhoneOptOut>(number);
    }

    // A number nothing else in the suite texts or stops: the fixed ones end 0001 to 0872 or in 1xxxx.
    private static string NewNumber() => $"+155501{Random.Shared.Next(30_000, 99_999)}";

    private async Task<NotificationPreferencesReading> ReadPreferences(HollandHogsSeason hogs)
    {
        var response = await Api.CreateClientFor(hogs.Sam).GetAsync($"/leagues/{hogs.LeagueId}/members/{hogs.Sams}/notifications");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<NotificationPreferencesReading>()).ShouldNotBeNull();
    }

    // As a member does: the number on record, then consent to be texted at it.
    private async Task OptIn(HollandHogsSeason hogs, string phone)
    {
        await RecordContact(hogs, phone);
        var response = await Api.CreateClientFor(hogs.Sam).PutAsJsonAsync(
            $"/leagues/{hogs.LeagueId}/members/{hogs.Sams}/notifications", new NotificationPreferencesRequest(TextMe: true));
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private async Task RecordContact(HollandHogsSeason hogs, string phone)
    {
        var response = await Api.CreateClientFor(hogs.Sam).PutAsJsonAsync(
            $"/leagues/{hogs.LeagueId}/members/{hogs.Sams}/contact", new ContactDetailsRequest("member@example.com", phone));
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    // Opening the season tells each member of the dues assessed to them; no member has opted in yet, so those texts
    // are skipped, and they are settled before a test goes on to opt a member in.
    private async Task<HollandHogsSeason> OpenSeason()
    {
        HollandHogsSeason? hogs = null;
        await Quiesced(async () => hogs = await HollandHogsSeason.Open(Api));
        return hogs!;
    }

    private async Task<Guid> AssessSam(HollandHogsSeason hogs)
    {
        var assessment = Guid.Empty;
        await Quiesced(async () => assessment = await new TreasurersDesk(Api, hogs)
            .Assess([hogs.Sams], 25m, new DateOnly(2026, 11, 1), "Trophy fund"));
        return assessment;
    }

    private async Task<Notification> NotificationFor(HollandHogsSeason hogs, Guid assessment)
    {
        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        return (await session.LoadAsync<Notification>(
            NotificationKey.For(NotificationKinds.DuesAssessed, assessment, Channels.Sms, hogs.Sams))).ShouldNotBeNull();
    }

    // Runs the action and waits until every message it caused, and every message those caused, has been handled.
    private async Task<ITrackedSession> Quiesced(Func<Task> action) =>
        await Api.Services.GetRequiredService<IHost>()
            .TrackActivity()
            .Timeout(TimeSpan.FromSeconds(30))
            .DoNotAssertOnExceptionsDetected()
            .ExecuteAndWaitAsync(_ => action());

    /// <summary>A text Twilio accepted, and the address it was asked to report on it to.</summary>
    private sealed record SentStatusText(HollandHogsSeason Hogs, Notification Notification, string Url);
}

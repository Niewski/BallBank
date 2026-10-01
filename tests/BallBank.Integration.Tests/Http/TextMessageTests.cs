using System.Net;
using System.Net.Http.Json;
using System.Web;
using BallBank.Api.Features.Membership;
using BallBank.Api.Features.Notifications;
using BallBank.Api.Features.Treasury;
using BallBank.Api.Integrations.Twilio;
using BallBank.Domain.Notifications;
using BallBank.Domain.Treasury;
using BallBank.Integration.Tests.Discord;
using BallBank.Integration.Tests.Twilio;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine.Tracking;

namespace BallBank.Integration.Tests.Http;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class TextMessageTests(PostgresFixture postgres)
{
    private const string SamsPhone = "+15550100002";
    private const string JacobsPhone = "+15550100001";

    // Mid-morning Eastern, outside the quiet hours a member has until they choose.
    private static readonly DateTimeOffset Monday = new(2026, 9, 28, 14, 0, 0, TimeSpan.Zero);

    private BallBankApi Api => postgres.Api;
    private FakeTwilio Twilio => Api.Twilio;

    [Fact]
    public async Task Assessing_dues_texts_the_member_who_opted_in_what_they_owe_and_when()
    {
        using var _ = Api.Clock.Set(Monday);
        var hogs = await OpenSeason();
        var desk = new TreasurersDesk(Api, hogs);
        await OptIn(hogs, hogs.Sam, hogs.Sams, "555 010 0002");

        var assessment = await AssessSam(hogs, desk);

        var texts = await Twilio.WaitForTexts(hogs.LeagueId, 1);
        var text = texts.ShouldHaveSingleItem();
        text.To.ShouldBe(SamsPhone);
        text.From.ShouldBe(Twilio.FromNumber);
        text.Body.ShouldBe(SmsTexts.DuesAssessed("Holland Hogs", 25m, "Trophy fund", new DateOnly(2026, 11, 1), StatementLink(hogs, hogs.Sams)));
    }

    [Fact]
    public async Task Each_text_asks_Twilio_to_report_to_the_league_and_the_notification()
    {
        using var _ = Api.Clock.Set(Monday);
        var hogs = await OpenSeason();
        var desk = new TreasurersDesk(Api, hogs);
        await OptIn(hogs, hogs.Sam, hogs.Sams, "555 010 0002");

        var assessment = await AssessSam(hogs, desk);

        var text = (await Twilio.WaitForTexts(hogs.LeagueId, 1)).Single();
        var notification = await NotificationFor(hogs, NotificationKinds.DuesAssessed, assessment, hogs.Sams);
        text.StatusCallback.AbsolutePath.ShouldBe("/webhooks/twilio/status");
        text.StatusCallback.GetLeftPart(UriPartial.Authority).ShouldBe(FakeTwilio.StatusCallbackBaseUrl);
        var query = HttpUtility.ParseQueryString(text.StatusCallback.Query);
        query["league"].ShouldBe(hogs.LeagueId.ToString());
        query["notification"].ShouldBe(notification.Id);
        text.StatusCallback.ToString().ShouldBe(TwilioSmsChannel.StatusCallback(FakeTwilio.StatusCallbackBaseUrl, hogs.LeagueId, notification.Id));
        notification.Status.ShouldBe(NotificationStatus.Sent);
        notification.Channel.ShouldBe(Channels.Sms);
        notification.MemberId.ShouldBe(hogs.Sams);
        notification.SentAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task An_attestation_texts_the_treasurers_who_did_not_attest_it()
    {
        using var _ = Api.Clock.Set(Monday);
        var hogs = await OpenSeason();
        var desk = new TreasurersDesk(Api, hogs);
        await OptIn(hogs, hogs.Jacob, hogs.Jacobs, "555 010 0001");
        await OptIn(hogs, hogs.Sam, hogs.Sams, "555 010 0002");

        await Quiesced(() => desk.Attest(hogs.Sam, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1"));
        await Quiesced(() => desk.Attest(hogs.Jacob, hogs.Jacobs, 50m, PaymentRail.Cash, null));

        // Sam is no treasurer, and Jacob needs no telling of the payment he attested himself.
        var texts = await Twilio.WaitForTexts(hogs.LeagueId, 1);
        var text = texts.ShouldHaveSingleItem();
        text.To.ShouldBe(JacobsPhone);
        text.Body.ShouldBe(SmsTexts.PaymentAttested("Holland Hogs", "Sam", 50m, PaymentRail.Venmo, "VN-1", StatementLink(hogs, hogs.Sams)));
    }

    [Fact]
    public async Task Confirming_texts_the_member_unless_they_confirmed_it_themselves()
    {
        using var _ = Api.Clock.Set(Monday);
        var hogs = await OpenSeason();
        var desk = new TreasurersDesk(Api, hogs);
        await OptIn(hogs, hogs.Jacob, hogs.Jacobs, "555 010 0001");
        await OptIn(hogs, hogs.Sam, hogs.Sams, "555 010 0002");
        var sams = await desk.Attest(hogs.Sam, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1");
        var jacobs = await desk.Attest(hogs.Jacob, hogs.Jacobs, 50m, PaymentRail.Cash, null);

        await Quiesced(() => desk.Confirm(hogs.Jacobs, jacobs));
        await Quiesced(() => desk.Confirm(hogs.Sams, sams));

        var texts = Twilio.Sent(hogs.LeagueId).Where(text => text.To == SamsPhone || text.To == JacobsPhone).ToList();
        texts.Select(text => (text.To, text.Body)).ShouldContain(
            (SamsPhone, SmsTexts.PaymentConfirmed("Holland Hogs", 50m, PaymentRail.Venmo, StatementLink(hogs, hogs.Sams))));
        texts.Where(text => text.Body.Contains("confirmed")).Select(text => text.To).ShouldBe([SamsPhone]);
    }

    [Fact]
    public async Task Rejecting_texts_the_member_why_unless_they_rejected_it_themselves()
    {
        using var _ = Api.Clock.Set(Monday);
        var hogs = await OpenSeason();
        var desk = new TreasurersDesk(Api, hogs);
        await OptIn(hogs, hogs.Jacob, hogs.Jacobs, "555 010 0001");
        await OptIn(hogs, hogs.Sam, hogs.Sams, "555 010 0002");
        var sams = await desk.Attest(hogs.Sam, hogs.Sams, 20m, PaymentRail.Zelle, "ZL-0");
        var jacobs = await desk.Attest(hogs.Jacob, hogs.Jacobs, 20m, PaymentRail.Cash, null);

        await Quiesced(() => desk.Reject(hogs.Jacobs, jacobs, "Counted twice"));
        await Quiesced(() => desk.Reject(hogs.Sams, sams, "Nothing arrived"));

        var texts = Twilio.Sent(hogs.LeagueId).Where(text => text.Body.Contains("rejected")).ToList();
        var text = texts.ShouldHaveSingleItem();
        text.To.ShouldBe(SamsPhone);
        text.Body.ShouldBe(SmsTexts.PaymentRejected("Holland Hogs", 20m, PaymentRail.Zelle, "Nothing arrived", StatementLink(hogs, hogs.Sams)));
    }

    [Fact]
    public async Task Adjusting_texts_the_member_why_unless_they_posted_it_themselves()
    {
        using var _ = Api.Clock.Set(Monday);
        var hogs = await OpenSeason();
        var desk = new TreasurersDesk(Api, hogs);
        await OptIn(hogs, hogs.Jacob, hogs.Jacobs, "555 010 0001");
        await OptIn(hogs, hogs.Sam, hogs.Sams, "555 010 0002");

        await Quiesced(() => desk.Adjust(hogs.Jacobs, -5m, "Waived: hosted the draft"));
        await Quiesced(() => desk.Adjust(hogs.Sams, -5m, "Waived: hosted the draft"));

        var text = (await Twilio.WaitForTexts(hogs.LeagueId, 1)).Single();
        text.To.ShouldBe(SamsPhone);
        text.Body.ShouldBe(SmsTexts.AdjustmentPosted("Holland Hogs", -5m, "Waived: hosted the draft", refund: false, StatementLink(hogs, hogs.Sams)));
    }

    [Fact]
    public async Task A_member_who_did_not_opt_in_is_not_texted_and_the_notification_is_skipped()
    {
        using var _ = Api.Clock.Set(Monday);
        var hogs = await OpenSeason();
        var desk = new TreasurersDesk(Api, hogs);

        var assessment = await AssessSam(hogs, desk);

        var notification = await NotificationFor(hogs, NotificationKinds.DuesAssessed, assessment, hogs.Sams);
        notification.Status.ShouldBe(NotificationStatus.Skipped);
        notification.Reason.ShouldBe(SmsDelivery.NoConsent);
        Twilio.Attempts(hogs.LeagueId).ShouldBe(0);
    }

    [Fact]
    public async Task Consent_for_a_number_the_member_has_since_replaced_is_no_consent()
    {
        using var _ = Api.Clock.Set(Monday);
        var hogs = await OpenSeason();
        var desk = new TreasurersDesk(Api, hogs);
        await OptIn(hogs, hogs.Sam, hogs.Sams, "555 010 0002");
        await RecordContact(hogs, hogs.Sam, hogs.Sams, "555 010 0003");

        var assessment = await AssessSam(hogs, desk);

        (await NotificationFor(hogs, NotificationKinds.DuesAssessed, assessment, hogs.Sams)).Status.ShouldBe(NotificationStatus.Skipped);
        Twilio.Attempts(hogs.LeagueId).ShouldBe(0);
    }

    [Fact]
    public async Task A_number_that_has_opted_out_is_not_texted_whatever_consent_says()
    {
        const string Number = "+15550100872";
        using var _ = Api.Clock.Set(Monday);
        var hogs = await OpenSeason();
        var desk = new TreasurersDesk(Api, hogs);
        await OptIn(hogs, hogs.Sam, hogs.Sams, "555 010 0872");
        await using (var session = Store.LightweightSession())
        {
            session.Store(new PhoneOptOut { Id = Number, OptedOutAt = Monday });
            await session.SaveChangesAsync();
        }

        try
        {
            var assessment = await AssessSam(hogs, desk);

            var notification = await NotificationFor(hogs, NotificationKinds.DuesAssessed, assessment, hogs.Sams);
            notification.Status.ShouldBe(NotificationStatus.Skipped);
            notification.Reason.ShouldBe(SmsDelivery.OptedOut);
            Twilio.Attempts(hogs.LeagueId).ShouldBe(0);
        }
        finally
        {
            await using var session = Store.LightweightSession();
            session.Delete<PhoneOptOut>(Number);
            await session.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task A_text_in_the_members_quiet_hours_is_held_until_they_end_and_then_sent()
    {
        // 11pm Sunday in Eastern, in the quiet hours a member has until they choose; they end at 9am.
        var night = new DateTimeOffset(2026, 9, 28, 3, 0, 0, TimeSpan.Zero);
        var hogs = await OpenSeason();
        var desk = new TreasurersDesk(Api, hogs);
        using (Api.Clock.Set(Monday))
        {
            await OptIn(hogs, hogs.Sam, hogs.Sams, "555 010 0002");
        }

        using var _ = Api.Clock.Set(night);
        var assessment = await AssessSam(hogs, desk);

        var held = await NotificationFor(hogs, NotificationKinds.DuesAssessed, assessment, hogs.Sams);
        held.Status.ShouldBe(NotificationStatus.Held);
        held.Reason.ShouldBe(SmsDelivery.InQuietHours);
        held.SendAfter.ShouldBe(new DateTimeOffset(2026, 9, 28, 13, 0, 0, TimeSpan.Zero));
        Twilio.Attempts(hogs.LeagueId).ShouldBe(0);

        // The send is delivered again once the hours have ended, as the tick that releases held texts will.
        using (Api.Clock.Set(Monday))
        {
            await Quiesced(() => Deliver(hogs, held.Id));
        }

        var sent = await NotificationFor(hogs, NotificationKinds.DuesAssessed, assessment, hogs.Sams);
        sent.Status.ShouldBe(NotificationStatus.Sent);
        sent.Reason.ShouldBeNull();
        sent.SendAfter.ShouldBeNull();
        Twilio.SentTo(hogs.LeagueId, SamsPhone).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_same_event_handled_twice_texts_once()
    {
        using var _ = Api.Clock.Set(Monday);
        var hogs = await OpenSeason();
        var desk = new TreasurersDesk(Api, hogs);
        await OptIn(hogs, hogs.Sam, hogs.Sams, "555 010 0002");
        var payment = await desk.Attest(hogs.Sam, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1");
        await Quiesced(() => desk.Confirm(hogs.Sams, payment));
        await Twilio.WaitForTexts(hogs.LeagueId, 1);

        // The message the store forwarded is delivered again, as it is when a node dies before acknowledging it.
        var confirmed = await ConfirmedEventOf(hogs, payment);
        var redelivered = await Quiesced(async () =>
        {
            var bus = Api.Services.GetRequiredService<Wolverine.IMessageBus>();
            var tenant = new Wolverine.DeliveryOptions { TenantId = hogs.LeagueId.ToString() };
            await bus.PublishAsync(confirmed, tenant);
            await bus.PublishAsync(confirmed, tenant);
        });

        redelivered.Executed.MessagesOf<JasperFx.Events.IEvent<PaymentConfirmed>>().Count().ShouldBe(2);
        Twilio.Sent(hogs.LeagueId).Count.ShouldBe(1);
        Twilio.Attempts(hogs.LeagueId).ShouldBe(1);
    }

    [Fact]
    public async Task A_confirmation_is_told_to_the_league_in_Discord_and_to_the_member_by_text()
    {
        using var _ = Api.Clock.Set(Monday);
        var hogs = await OpenSeason();
        var desk = new TreasurersDesk(Api, hogs);
        var webhook = Api.Discord.NewWebhook();
        (await Api.CreateClientFor(hogs.Jacob).PutAsJsonAsync(
            $"/leagues/{hogs.LeagueId}/notifications/discord", new DiscordSettingsRequest(webhook.Url, true, PostDigest: false)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        await OptIn(hogs, hogs.Sam, hogs.Sams, "555 010 0002");
        var payment = await desk.Attest(hogs.Sam, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1");

        await Quiesced(() => desk.Confirm(hogs.Sams, payment));

        (await webhook.WaitForPosts(2)).Count.ShouldBe(2);
        Twilio.SentTo(hogs.LeagueId, SamsPhone).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_failing_Twilio_does_not_fail_the_command_and_the_send_is_dead_lettered()
    {
        using var _ = Api.Clock.Set(Monday);
        var hogs = await OpenSeason();
        var desk = new TreasurersDesk(Api, hogs);
        await OptIn(hogs, hogs.Sam, hogs.Sams, "555 010 0002");
        Twilio.RefusesWith(hogs.LeagueId, HttpStatusCode.InternalServerError);
        var payment = await desk.Attest(hogs.Sam, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1");

        await Quiesced(() => desk.Confirm(hogs.Sams, payment));

        var notification = await NotificationFor(hogs, NotificationKinds.PaymentConfirmed, payment, hogs.Sams);
        notification.Status.ShouldBe(NotificationStatus.Pending);
        Twilio.Attempts(hogs.LeagueId).ShouldBeGreaterThan(2);
        Twilio.Sent(hogs.LeagueId).ShouldBeEmpty();
        (await DeadLetterCount(notification.Id)).ShouldBe(1);

        // What the confirmation did stands.
        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        (await session.LoadAsync<MemberStatement>(hogs.AccountOf(hogs.Sams))).ShouldNotBeNull()
            .Totals().Confirmed.ShouldBe(50m);
    }

    [Fact]
    public async Task Neither_the_auth_token_nor_the_number_appears_in_a_log_entry_or_a_dead_letter()
    {
        using var _ = Api.Clock.Set(Monday);
        var hogs = await OpenSeason();
        var desk = new TreasurersDesk(Api, hogs);
        await OptIn(hogs, hogs.Sam, hogs.Sams, "555 010 0002");
        Twilio.RefusesWith(hogs.LeagueId, HttpStatusCode.InternalServerError);
        var payment = await desk.Attest(hogs.Sam, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1");

        await Quiesced(() => desk.Confirm(hogs.Sams, payment));

        var notification = await NotificationFor(hogs, NotificationKinds.PaymentConfirmed, payment, hogs.Sams);
        (await DeadLetterCount(notification.Id)).ShouldBe(1);
        string[] secrets = [Twilio.AuthToken, Twilio.AccountSid, SamsPhone, "5550100002"];
        var letters = await DeadLetters(hogs.LeagueId);
        letters.ShouldNotBeEmpty();
        letters.Where(letter => secrets.Any(secret => letter.Body.Contains(secret) || letter.Failure?.Contains(secret) == true)).ShouldBeEmpty();
        Api.Logs.All.Where(entry => secrets.Any(secret =>
                entry.Message.Contains(secret) || entry.Scope.Values.Any(value => value?.ToString()?.Contains(secret) == true)))
            .ShouldBeEmpty();
    }

    private IDocumentStore Store => Api.Services.GetRequiredService<IDocumentStore>();

    private static string StatementLink(HollandHogsSeason hogs, Guid memberId) =>
        SmsTexts.StatementLink(BallBankApi.WebBaseUrl, hogs.LeagueId, hogs.AccountOf(memberId));

    // As a member does: the number on record, then consent to be texted at it.
    private async Task OptIn(HollandHogsSeason hogs, string subject, Guid memberId, string phone)
    {
        await RecordContact(hogs, subject, memberId, phone);
        var response = await Api.CreateClientFor(subject).PutAsJsonAsync(
            $"/leagues/{hogs.LeagueId}/members/{memberId}/notifications", new NotificationPreferencesRequest(TextMe: true));
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private async Task RecordContact(HollandHogsSeason hogs, string subject, Guid memberId, string phone)
    {
        var response = await Api.CreateClientFor(subject).PutAsJsonAsync(
            $"/leagues/{hogs.LeagueId}/members/{memberId}/contact", new ContactDetailsRequest("member@example.com", phone));
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private async Task Deliver(HollandHogsSeason hogs, string notificationId)
    {
        var bus = Api.Services.GetRequiredService<Wolverine.IMessageBus>();
        await bus.PublishAsync(
            new SendNotification(hogs.LeagueId, notificationId),
            new Wolverine.DeliveryOptions { TenantId = hogs.LeagueId.ToString() });
    }

    // Runs the action and waits until every message it caused, and every message those caused, has been handled.
    private async Task<ITrackedSession> Quiesced(Func<Task> action) =>
        await Api.Services.GetRequiredService<IHost>()
            .TrackActivity()
            .Timeout(TimeSpan.FromSeconds(30))
            .DoNotAssertOnExceptionsDetected()
            .ExecuteAndWaitAsync(_ => action());

    private async Task<JasperFx.Events.IEvent<PaymentConfirmed>> ConfirmedEventOf(HollandHogsSeason hogs, Guid attestationId)
    {
        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        var events = await session.Events.FetchStreamAsync(hogs.AccountOf(hogs.Sams));
        return events.OfType<JasperFx.Events.IEvent<PaymentConfirmed>>().Single(e => e.Data.AttestationId == attestationId);
    }

    // Opening the season tells each member of the dues assessed to them; no member has opted in yet, so those texts
    // are skipped, and they are settled before a test goes on to opt a member in.
    private async Task<HollandHogsSeason> OpenSeason()
    {
        HollandHogsSeason? hogs = null;
        await Quiesced(async () => hogs = await HollandHogsSeason.Open(Api));
        return hogs!;
    }

    private async Task<Guid> AssessSam(HollandHogsSeason hogs, TreasurersDesk desk)
    {
        var assessment = Guid.Empty;
        await Quiesced(async () => assessment = await desk.Assess([hogs.Sams], 25m, new DateOnly(2026, 11, 1), "Trophy fund"));
        return assessment;
    }

    // The text notification the member's event caused, found by the key it is recorded under.
    private async Task<Notification> NotificationFor(HollandHogsSeason hogs, string kind, Guid cause, Guid memberId)
    {
        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        return (await session.LoadAsync<Notification>(NotificationKey.For(kind, cause, Channels.Sms, memberId))).ShouldNotBeNull();
    }

    // A message is dead-lettered a moment after its last attempt, so this waits for it.
    private async Task<long> DeadLetterCount(string bodyFragment)
    {
        await using var connection = new Npgsql.NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select count(*) from ballbank.wolverine_dead_letters where message_type like '%SendNotification%' and encode(body, 'escape') like @fragment";
        command.Parameters.AddWithValue("fragment", $"%{bodyFragment}%");

        var deadline = DateTime.UtcNow.AddSeconds(15);
        var count = (long)(await command.ExecuteScalarAsync())!;
        while (count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
            count = (long)(await command.ExecuteScalarAsync())!;
        }

        return count;
    }

    private async Task<List<DeadLetter>> DeadLetters(Guid leagueId)
    {
        await using var connection = new Npgsql.NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select encode(body, 'escape'), exception_message from ballbank.wolverine_dead_letters where message_type like '%SendNotification%' and encode(body, 'escape') like @league";
        command.Parameters.AddWithValue("league", $"%{leagueId}%");

        var letters = new List<DeadLetter>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            letters.Add(new DeadLetter(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
        }

        return letters;
    }

    private sealed record DeadLetter(string Body, string? Failure);
}

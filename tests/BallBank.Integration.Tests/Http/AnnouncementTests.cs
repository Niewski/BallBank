using System.Net;
using System.Net.Http.Json;
using BallBank.Api;
using BallBank.Api.Features.Notifications;
using BallBank.Api.Features.Treasury;
using BallBank.Domain.Notifications;
using BallBank.Domain.Treasury;
using BallBank.Integration.Tests.Discord;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine.Tracking;

namespace BallBank.Integration.Tests.Http;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class AnnouncementTests(PostgresFixture postgres)
{
    private const string TeamName = "Sam's Slammers";

    private BallBankApi Api => postgres.Api;

    [Fact]
    public async Task Opening_a_season_announces_it_with_the_dues_and_the_due_date()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var webhook = await Connect(hogs, announcePayments: false);
        var due = new DateOnly(2027, 10, 1);

        await Quiesced(() => OpenSeason(hogs, "2027", 75m, due));

        var posted = await webhook.WaitForPosts(2);
        posted[1].Content.ShouldBe(NotificationTexts.SeasonOpened("Holland Hogs", "2027", 75m, due));
        posted[1].Payload["allowed_mentions"]!["parse"]!.AsArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task Opening_a_season_that_is_already_open_announces_nothing_more()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var webhook = await Connect(hogs, announcePayments: false);

        await Quiesced(() => OpenSeason(hogs, "2027", 75m, new DateOnly(2027, 10, 1)));
        await Quiesced(() => OpenSeason(hogs, "2027", 75m, new DateOnly(2027, 10, 1)));

        webhook.Posted.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Confirming_a_payment_announces_the_team_the_amount_and_the_pot_now()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var desk = new TreasurersDesk(Api, hogs);
        var webhook = await Connect(hogs, announcePayments: true);

        // Sam overpays the $50 dues by 10, which is paid back; the pot is what the accounts hold now.
        var first = await desk.Attest(hogs.Sam, hogs.Sams, 60m, PaymentRail.Venmo, "VN-1");
        await Quiesced(() => desk.Confirm(hogs.Sams, first));
        await desk.Adjust(hogs.Sams, 10m, "Refund of the overpayment", refund: true);
        var second = await desk.Attest(hogs.Jacob, hogs.Jacobs, 30m, PaymentRail.Cash, null);
        await Quiesced(() => desk.Confirm(hogs.Jacobs, second));

        var posted = await webhook.WaitForPosts(3);
        posted.Skip(1).Select(p => p.Content).ShouldBe(
        [
            NotificationTexts.PaymentConfirmed(TeamName, 60m, pot: 60m),
            NotificationTexts.PaymentConfirmed("Hog Wild", 30m, pot: 80m),
        ]);
    }

    [Fact]
    public async Task Nothing_is_announced_about_payments_when_announcing_is_off()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var desk = new TreasurersDesk(Api, hogs);
        var webhook = await Connect(hogs, announcePayments: false);

        var payment = await desk.Attest(hogs.Sam, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1");
        await Quiesced(() => desk.Confirm(hogs.Sams, payment));

        webhook.Posted.Select(p => p.Content).ShouldBe([NotificationTexts.Hello("Holland Hogs")]);
    }

    [Fact]
    public async Task Attesting_and_rejecting_announce_nothing_however_announcing_is_set()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var desk = new TreasurersDesk(Api, hogs);
        var webhook = await Connect(hogs, announcePayments: true);

        await Quiesced(async () =>
        {
            var payment = await desk.Attest(hogs.Sam, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1");
            await desk.Reject(hogs.Sams, payment, "Nothing arrived");
            await desk.Adjust(hogs.Sams, -5m, "Waived: hosted the draft");
        });

        webhook.Posted.Select(p => p.Content).ShouldBe([NotificationTexts.Hello("Holland Hogs")]);
    }

    [Fact]
    public async Task Nothing_is_posted_once_Discord_is_disconnected()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var desk = new TreasurersDesk(Api, hogs);
        var webhook = await Connect(hogs, announcePayments: true);
        (await Api.CreateClientFor(hogs.Jacob).DeleteAsync(SettingsPath(hogs))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var payment = await desk.Attest(hogs.Sam, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1");
        await Quiesced(async () =>
        {
            await desk.Confirm(hogs.Sams, payment);
            await OpenSeason(hogs, "2027", 75m, new DateOnly(2027, 10, 1));
        });

        webhook.Posted.Select(p => p.Content).ShouldBe([NotificationTexts.Hello("Holland Hogs")]);
    }

    [Fact]
    public async Task The_same_event_handled_twice_announces_once()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var desk = new TreasurersDesk(Api, hogs);
        var webhook = await Connect(hogs, announcePayments: true);
        var payment = await desk.Attest(hogs.Sam, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1");
        await Quiesced(() => desk.Confirm(hogs.Sams, payment));
        await webhook.WaitForPosts(2);

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
        webhook.Posted.Count.ShouldBe(2);
        webhook.Attempts.ShouldBe(2);
    }

    [Fact]
    public async Task A_failing_Discord_does_not_fail_the_confirmation_and_the_send_is_dead_lettered()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var desk = new TreasurersDesk(Api, hogs);
        var webhook = await Connect(hogs, announcePayments: true);
        webhook.RefusesWith(HttpStatusCode.InternalServerError);
        var payment = await desk.Attest(hogs.Sam, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1");

        await Quiesced(() => desk.Confirm(hogs.Sams, payment));

        var notification = await NotificationOf(hogs, NotificationKinds.PaymentConfirmed, payment);
        notification.Status.ShouldBe(NotificationStatus.Pending);
        notification.Text.ShouldBe(NotificationTexts.PaymentConfirmed(TeamName, 50m, pot: 50m));
        webhook.Attempts.ShouldBeGreaterThan(2);
        webhook.Posted.Count.ShouldBe(1);
        (await DeadLetterCount(notification.Id)).ShouldBe(1);

        // What the confirmation did stands.
        await using var session = Api.Services.GetRequiredService<IDocumentStore>().QuerySession(hogs.LeagueId.ToString());
        (await session.LoadAsync<MemberStatement>(hogs.AccountOf(hogs.Sams))).ShouldNotBeNull()
            .Totals().Confirmed.ShouldBe(50m);
    }

    [Fact]
    public async Task A_notification_is_recorded_as_sent_once_Discord_has_taken_it()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var desk = new TreasurersDesk(Api, hogs);
        var webhook = await Connect(hogs, announcePayments: true);
        var payment = await desk.Attest(hogs.Sam, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1");

        await Quiesced(() => desk.Confirm(hogs.Sams, payment));

        var notification = await NotificationOf(hogs, NotificationKinds.PaymentConfirmed, payment);
        notification.Status.ShouldBe(NotificationStatus.Sent);
        notification.Channel.ShouldBe(Channels.Discord);
        notification.SentAt.ShouldNotBeNull();
        notification.Id.ShouldBe(NotificationKey.For(NotificationKinds.PaymentConfirmed, payment, Channels.Discord, hogs.LeagueId));
        (await webhook.WaitForPosts(2)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_webhook_appears_in_no_log_entry()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var desk = new TreasurersDesk(Api, hogs);
        var webhook = await Connect(hogs, announcePayments: true);
        var secret = webhook.Url[(webhook.Url.LastIndexOf('/') + 1)..];
        var payment = await desk.Attest(hogs.Sam, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1");

        await Quiesced(() => desk.Confirm(hogs.Sams, payment));
        webhook.RefusesWith(HttpStatusCode.InternalServerError);
        await Quiesced(() => OpenSeason(hogs, "2027", 75m, new DateOnly(2027, 10, 1)));

        // The refused send was retried and dead-lettered, so what the failure left behind is checked too.
        (await DeadLetterCount(hogs.LeagueId.ToString())).ShouldBeGreaterThan(0);
        var deadLetters = await SendDeadLetters(hogs.LeagueId);
        deadLetters.ShouldNotBeEmpty();
        deadLetters.Where(letter => letter.Body.Contains(secret) || letter.Failure?.Contains(secret) == true).ShouldBeEmpty();
        Api.Logs.All.Where(entry => entry.Message.Contains(secret) || entry.Scope.Values.Any(v => v?.ToString()?.Contains(secret) == true))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task A_notification_and_the_send_that_delivers_it_commit_together_or_not_at_all()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var desk = new TreasurersDesk(Api, hogs);
        var webhook = await Connect(hogs, announcePayments: true);
        var payment = await desk.Attest(hogs.Sam, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1");

        ITrackedSession tracked;
        using (Api.NotificationCommits.Hold(hogs.LeagueId))
        {
            tracked = await Quiesced(() => desk.Confirm(hogs.Sams, payment));
        }

        tracked.Executed.MessagesOf<JasperFx.Events.IEvent<PaymentConfirmed>>().ShouldNotBeEmpty();
        tracked.Sent.MessagesOf<SendNotification>().ShouldBeEmpty();
        (await FindNotification(hogs, NotificationKinds.PaymentConfirmed, payment)).ShouldBeNull();
        webhook.Posted.Select(p => p.Content).ShouldBe([NotificationTexts.Hello("Holland Hogs")]);

        // What the confirmation did stands.
        await using var session = Api.Services.GetRequiredService<IDocumentStore>().QuerySession(hogs.LeagueId.ToString());
        (await session.LoadAsync<MemberStatement>(hogs.AccountOf(hogs.Sams))).ShouldNotBeNull()
            .Totals().Confirmed.ShouldBe(50m);
    }

    [Fact]
    public async Task A_notification_decided_before_the_league_disconnected_is_dropped_not_posted()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var desk = new TreasurersDesk(Api, hogs);
        var webhook = await Connect(hogs, announcePayments: true);
        webhook.RefusesWith(HttpStatusCode.InternalServerError);
        var payment = await desk.Attest(hogs.Sam, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1");
        await Quiesced(() => desk.Confirm(hogs.Sams, payment));
        var notification = await NotificationOf(hogs, NotificationKinds.PaymentConfirmed, payment);
        notification.Status.ShouldBe(NotificationStatus.Pending);

        // The league disconnects, the channel would take the post now, and the stranded send is delivered again.
        (await Api.CreateClientFor(hogs.Jacob).DeleteAsync(SettingsPath(hogs))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        webhook.Accepts();
        var attempts = webhook.Attempts;
        await Quiesced(async () =>
        {
            var bus = Api.Services.GetRequiredService<Wolverine.IMessageBus>();
            var tenant = new Wolverine.DeliveryOptions { TenantId = hogs.LeagueId.ToString() };
            await bus.PublishAsync(new SendNotification(hogs.LeagueId, notification.Id), tenant);
        });

        (await NotificationOf(hogs, NotificationKinds.PaymentConfirmed, payment)).Status.ShouldBe(NotificationStatus.Dropped);
        webhook.Attempts.ShouldBe(attempts);
        webhook.Posted.Select(p => p.Content).ShouldBe([NotificationTexts.Hello("Holland Hogs")]);
    }

    private static string SettingsPath(HollandHogsSeason hogs) => $"/leagues/{hogs.LeagueId}/notifications/discord";

    // Connects Discord as the treasurer does; the hello it posts is the webhook's first message.
    private async Task<FakeDiscord.Webhook> Connect(HollandHogsSeason hogs, bool announcePayments)
    {
        var webhook = Api.Discord.NewWebhook();
        var response = await Api.CreateClientFor(hogs.Jacob)
            .PutAsJsonAsync(SettingsPath(hogs), new DiscordSettingsRequest(webhook.Url, announcePayments, PostDigest: false));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return webhook;
    }

    private async Task OpenSeason(HollandHogsSeason hogs, string label, decimal dues, DateOnly dueDate)
    {
        var open = new HttpRequestMessage(HttpMethod.Post, $"/leagues/{hogs.LeagueId}/seasons")
        {
            Content = JsonContent.Create(new OpenSeasonRequest(label, dues, dueDate)),
        };
        open.Headers.Add(Idempotency.Header, Guid.NewGuid().ToString());
        (await Api.CreateClientFor(hogs.Jacob).SendAsync(open)).IsSuccessStatusCode.ShouldBeTrue();
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
        await using var session = Api.Services.GetRequiredService<IDocumentStore>().QuerySession(hogs.LeagueId.ToString());
        var events = await session.Events.FetchStreamAsync(hogs.AccountOf(hogs.Sams));
        return events.OfType<JasperFx.Events.IEvent<PaymentConfirmed>>().Single(e => e.Data.AttestationId == attestationId);
    }

    private async Task<Notification> NotificationOf(HollandHogsSeason hogs, string kind, Guid cause) =>
        (await FindNotification(hogs, kind, cause)).ShouldNotBeNull();

    private async Task<Notification?> FindNotification(HollandHogsSeason hogs, string kind, Guid cause)
    {
        await using var session = Api.Services.GetRequiredService<IDocumentStore>().QuerySession(hogs.LeagueId.ToString());
        return await session.LoadAsync<Notification>(NotificationKey.For(kind, cause, Channels.Discord, hogs.LeagueId));
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

    private async Task<List<DeadLetter>> SendDeadLetters(Guid leagueId)
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

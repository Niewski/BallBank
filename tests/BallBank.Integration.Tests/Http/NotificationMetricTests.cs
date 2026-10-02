using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using BallBank.Api;
using BallBank.Api.Features.Membership;
using BallBank.Api.Features.Notifications;
using BallBank.Domain.Notifications;
using BallBank.Domain.Treasury;
using BallBank.Integration.Tests.Discord;
using BallBank.Integration.Tests.Twilio;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Wolverine.Tracking;

namespace BallBank.Integration.Tests.Http;

/// <summary>
/// "Did anyone get texted this week?" and "what will Twilio charge?" are answered from a counter of what became of
/// each attempt to deliver a notification, by league, channel, kind and outcome.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class NotificationMetricTests(PostgresFixture postgres)
{
    private const string SamsPhone = "+15550100002";

    // Mid-morning Eastern, outside the quiet hours a member has until they choose; three days before the season's dues are due.
    private static readonly DateTimeOffset Morning = new(2026, 9, 28, 14, 0, 0, TimeSpan.Zero);

    // 11pm Sunday in Eastern, in those quiet hours; they end at 9am.
    private static readonly DateTimeOffset Night = new(2026, 9, 28, 3, 0, 0, TimeSpan.Zero);

    private BallBankApi Api => postgres.Api;
    private FakeTwilio Twilio => Api.Twilio;

    [Fact]
    public async Task A_notification_is_counted_by_its_channel_its_kind_and_what_became_of_it()
    {
        using var counted = new Counted();
        using var _ = Api.Clock.Set(Morning);
        var hogs = await OpenSeason();
        var desk = new TreasurersDesk(Api, hogs);
        await ConnectDiscord(hogs, announcePayments: true);
        await OptIn(hogs, hogs.Sam, hogs.Sams, "555 010 0002");
        var payment = await desk.Attest(hogs.Sam, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1");

        await Quiesced(() => desk.Confirm(hogs.Sams, payment));

        counted.Of(hogs, Channels.Discord, NotificationKinds.PaymentConfirmed, NotificationStatus.Sent).ShouldBe(1);
        counted.Of(hogs, Channels.Sms, NotificationKinds.PaymentConfirmed, NotificationStatus.Sent).ShouldBe(1);

        // Opening the season told every member what they owe, before any of them agreed to be texted.
        var unasked = await StoredCount(hogs, Channels.Sms, NotificationKinds.DuesAssessed, NotificationStatus.Skipped);
        unasked.ShouldBeGreaterThan(0);
        counted.Of(hogs, Channels.Sms, NotificationKinds.DuesAssessed, NotificationStatus.Skipped).ShouldBe(unasked);
        counted.Of(hogs, Channels.Sms, NotificationKinds.DuesAssessed, NotificationStatus.Sent).ShouldBe(0);
    }

    [Fact]
    public async Task A_text_in_the_quiet_hours_is_counted_as_held_and_when_it_goes_as_sent()
    {
        using var counted = new Counted();
        var hogs = await OpenSeason();
        var desk = new TreasurersDesk(Api, hogs);
        using (Api.Clock.Set(Morning))
        {
            await OptIn(hogs, hogs.Sam, hogs.Sams, "555 010 0002");
        }

        Guid assessment;
        using (Api.Clock.Set(Night))
        {
            assessment = await AssessSam(hogs, desk);

            counted.Of(hogs, Channels.Sms, NotificationKinds.DuesAssessed, NotificationStatus.Held).ShouldBe(1);
            counted.Of(hogs, Channels.Sms, NotificationKinds.DuesAssessed, NotificationStatus.Sent).ShouldBe(0);
        }

        using (Api.Clock.Set(Morning))
        {
            await Quiesced(() => Deliver(hogs, NotificationKey.For(NotificationKinds.DuesAssessed, assessment, Channels.Sms, hogs.Sams)));
        }

        counted.Of(hogs, Channels.Sms, NotificationKinds.DuesAssessed, NotificationStatus.Held).ShouldBe(1);
        counted.Of(hogs, Channels.Sms, NotificationKinds.DuesAssessed, NotificationStatus.Sent).ShouldBe(1);
    }

    [Fact]
    public async Task A_refused_send_is_counted_as_failed_each_time_and_one_the_league_disconnected_as_dropped()
    {
        using var counted = new Counted();
        var hogs = await HollandHogsSeason.Open(Api);
        var desk = new TreasurersDesk(Api, hogs);
        var webhook = await ConnectDiscord(hogs, announcePayments: true);
        webhook.RefusesWith(HttpStatusCode.InternalServerError);
        var payment = await desk.Attest(hogs.Sam, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1");

        await Quiesced(() => desk.Confirm(hogs.Sams, payment));

        // The send and each retry the queue schedules before it gives up.
        var refused = 1 + Api.Services.GetRequiredService<IOptions<NotificationOptions>>().Value.Delays.Length;
        counted.Of(hogs, Channels.Discord, NotificationKinds.PaymentConfirmed, NotificationMetric.Failed).ShouldBe(refused);
        counted.Of(hogs, Channels.Discord, NotificationKinds.PaymentConfirmed, NotificationStatus.Sent).ShouldBe(0);

        (await Api.CreateClientFor(hogs.Jacob).DeleteAsync(SettingsPath(hogs))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        webhook.Accepts();
        await Quiesced(() => Deliver(hogs, NotificationKey.For(NotificationKinds.PaymentConfirmed, payment, Channels.Discord, hogs.LeagueId)));

        counted.Of(hogs, Channels.Discord, NotificationKinds.PaymentConfirmed, NotificationStatus.Dropped).ShouldBe(1);
        counted.Of(hogs, Channels.Discord, NotificationKinds.PaymentConfirmed, NotificationStatus.Sent).ShouldBe(0);
        counted.Of(hogs, Channels.Discord, NotificationKinds.PaymentConfirmed, NotificationMetric.Failed).ShouldBe(refused);
    }

    [Fact]
    public async Task A_reminder_the_tick_could_not_get_through_is_counted_as_failed_and_when_it_does_as_sent()
    {
        using var counted = new Counted();
        using var _ = Api.Clock.Set(Morning);
        var hogs = await OpenSeason();
        await OptIn(hogs, hogs.Sam, hogs.Sams, "555 010 0002");
        Twilio.RefusesWith(hogs.LeagueId, HttpStatusCode.InternalServerError);

        await Api.Services.GetRequiredService<Tick>().RunAsync(hogs.LeagueId);

        counted.Of(hogs, Channels.Sms, NotificationKinds.Reminder, NotificationMetric.Failed).ShouldBe(1);
        counted.Of(hogs, Channels.Sms, NotificationKinds.Reminder, NotificationStatus.Sent).ShouldBe(0);

        Twilio.Accepts(hogs.LeagueId);
        await Api.Services.GetRequiredService<Tick>().RunAsync(hogs.LeagueId);

        counted.Of(hogs, Channels.Sms, NotificationKinds.Reminder, NotificationMetric.Failed).ShouldBe(1);
        counted.Of(hogs, Channels.Sms, NotificationKinds.Reminder, NotificationStatus.Sent).ShouldBe(1);
        Twilio.SentTo(hogs.LeagueId, SamsPhone).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_held_reminder_the_member_has_since_paid_for_is_counted_as_skipped()
    {
        using var counted = new Counted();
        var hogs = await OpenSeason();
        var desk = new TreasurersDesk(Api, hogs);
        using (Api.Clock.Set(Morning))
        {
            await OptIn(hogs, hogs.Sam, hogs.Sams, "555 010 0002");
        }

        // The night's tick holds Sam's reminder, and skips the members who never agreed to be texted.
        long skippedAtNight;
        using (Api.Clock.Set(Night))
        {
            await Api.Services.GetRequiredService<Tick>().RunAsync(hogs.LeagueId);
            counted.Of(hogs, Channels.Sms, NotificationKinds.Reminder, NotificationStatus.Held).ShouldBe(1);
            skippedAtNight = counted.Of(hogs, Channels.Sms, NotificationKinds.Reminder, NotificationStatus.Skipped);

            var payment = await desk.Attest(hogs.Sam, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1");
            await Quiesced(() => desk.Confirm(hogs.Sams, payment));
        }

        using (Api.Clock.Set(Morning))
        {
            await Api.Services.GetRequiredService<Tick>().RunAsync(hogs.LeagueId);
        }

        counted.Of(hogs, Channels.Sms, NotificationKinds.Reminder, NotificationStatus.Skipped).ShouldBe(skippedAtNight + 1);
        counted.Of(hogs, Channels.Sms, NotificationKinds.Reminder, NotificationStatus.Sent).ShouldBe(0);
    }

    private static string SettingsPath(HollandHogsSeason hogs) => $"/leagues/{hogs.LeagueId}/notifications/discord";

    private IDocumentStore Store => Api.Services.GetRequiredService<IDocumentStore>();

    private async Task<int> StoredCount(HollandHogsSeason hogs, string channel, string kind, string status)
    {
        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        return await session.Query<Notification>()
            .Where(notification => notification.Channel == channel && notification.Kind == kind && notification.Status == status)
            .CountAsync();
    }

    private async Task<FakeDiscord.Webhook> ConnectDiscord(HollandHogsSeason hogs, bool announcePayments)
    {
        var webhook = Api.Discord.NewWebhook();
        var response = await Api.CreateClientFor(hogs.Jacob)
            .PutAsJsonAsync(SettingsPath(hogs), new DiscordSettingsRequest(webhook.Url, announcePayments, PostDigest: false));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return webhook;
    }

    private async Task OptIn(HollandHogsSeason hogs, string subject, Guid memberId, string phone)
    {
        var client = Api.CreateClientFor(subject);
        (await client.PutAsJsonAsync($"/leagues/{hogs.LeagueId}/members/{memberId}/contact", new ContactDetailsRequest("member@example.com", phone)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PutAsJsonAsync($"/leagues/{hogs.LeagueId}/members/{memberId}/notifications", new NotificationPreferencesRequest(TextMe: true)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private async Task<Guid> AssessSam(HollandHogsSeason hogs, TreasurersDesk desk)
    {
        var assessment = Guid.Empty;
        await Quiesced(async () => assessment = await desk.Assess([hogs.Sams], 25m, new DateOnly(2026, 11, 1), "Trophy fund"));
        return assessment;
    }

    private async Task Deliver(HollandHogsSeason hogs, string notificationId)
    {
        var bus = Api.Services.GetRequiredService<Wolverine.IMessageBus>();
        await bus.PublishAsync(
            new SendNotification(hogs.LeagueId, notificationId),
            new Wolverine.DeliveryOptions { TenantId = hogs.LeagueId.ToString() });
    }

    // Opening a season texts every member (skipped: nobody has opted in yet); that settles before a test opts one in.
    private async Task<HollandHogsSeason> OpenSeason()
    {
        HollandHogsSeason? hogs = null;
        await Quiesced(async () => hogs = await HollandHogsSeason.Open(Api));
        return hogs!;
    }

    private async Task<ITrackedSession> Quiesced(Func<Task> action) =>
        await Api.Services.GetRequiredService<IHost>()
            .TrackActivity()
            .Timeout(TimeSpan.FromSeconds(30))
            .DoNotAssertOnExceptionsDetected()
            .ExecuteAndWaitAsync(_ => action());

    private sealed class Counted : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentQueue<(string? Tenant, string? Channel, string? Kind, string? Outcome, long Value)> _seen = new();

        public Counted()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == NotificationMetric.MeterName && instrument.Name == NotificationMetric.InstrumentName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            {
                string? tenant = null;
                string? channel = null;
                string? kind = null;
                string? outcome = null;
                foreach (var tag in tags)
                {
                    switch (tag.Key)
                    {
                        case TenantTelemetry.TenantId:
                            tenant = tag.Value?.ToString();
                            break;
                        case NotificationMetric.Channel:
                            channel = tag.Value?.ToString();
                            break;
                        case NotificationMetric.Kind:
                            kind = tag.Value?.ToString();
                            break;
                        case NotificationMetric.Outcome:
                            outcome = tag.Value?.ToString();
                            break;
                    }
                }

                _seen.Enqueue((tenant, channel, kind, outcome, value));
            });
            _listener.Start();
        }

        /// <summary>What was counted for the league under this channel, kind and outcome.</summary>
        public long Of(HollandHogsSeason hogs, string channel, string kind, string outcome) =>
            _seen
                .Where(measurement => measurement.Tenant == hogs.LeagueId.ToString()
                    && measurement.Channel == channel && measurement.Kind == kind && measurement.Outcome == outcome)
                .Sum(measurement => measurement.Value);

        public void Dispose() => _listener.Dispose();
    }
}

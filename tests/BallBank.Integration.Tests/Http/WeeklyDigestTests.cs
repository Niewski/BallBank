using System.Net;
using System.Net.Http.Json;
using BallBank.Api.Features.Notifications;
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
public class WeeklyDigestTests(PostgresFixture postgres)
{
    // Monday 09:00 in New York is 13:00 UTC while the clocks are forward; both weeks fall before the Oct 1 due date, so no reminder is in play.
    private static readonly DateTimeOffset SlotOfWeek38 = new(2026, 9, 14, 13, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SlotOfWeek39 = new(2026, 9, 21, 13, 0, 0, TimeSpan.Zero);
    private static readonly IsoWeek Week38 = new(2026, 38);
    private static readonly IsoWeek Week39 = new(2026, 39);

    private BallBankApi Api => postgres.Api;

    private IDocumentStore Store => Api.Services.GetRequiredService<IDocumentStore>();

    [Fact]
    public async Task The_first_tick_at_the_slot_posts_the_digest_to_the_channel()
    {
        var hogs = await OpenSeason();
        var webhook = await Connect(hogs, postDigest: true);

        using var _ = Api.Clock.Set(SlotOfWeek38);
        var summary = await RunTick(hogs);

        // Nobody has paid: all four members owe the $50 dues and the pot is empty.
        var digest = NotificationTexts.Digest(
            "Holland Hogs", "2026", [new("Hog Wild", 50m), new("Priya", 50m), new("Sam's Slammers", 50m), new("Team 4", 50m)], pot: 0m, attestationsPending: 0);
        PostedAfterTheHello(webhook).ShouldBe([digest]);
        summary.DigestsPosted.ShouldBe(1);
        summary.Failed.ShouldBe(0);

        var notification = (await FindDigest(hogs, Week38)).ShouldNotBeNull();
        notification.Id.ShouldBe($"Discord/{hogs.LeagueId}/Digest/2026-W38");
        notification.Kind.ShouldBe(NotificationKinds.Digest);
        notification.Channel.ShouldBe(Channels.Discord);
        notification.MemberId.ShouldBeNull();
        notification.Text.ShouldBe(digest);
        notification.Status.ShouldBe(NotificationStatus.Sent);
        notification.SentAt.ShouldBe(SlotOfWeek38);
    }

    [Fact]
    public async Task A_week_is_digested_once_from_its_slot_and_the_next_week_has_a_digest_of_its_own()
    {
        var hogs = await OpenSeason();
        var webhook = await Connect(hogs, postDigest: true);

        // A minute before the slot, the slot, a repeat, a late tick, Sunday 18:00 (the wake-up), then the same a week on.
        (DateTimeOffset Time, int Digests)[] ticks =
        [
            (SlotOfWeek38.AddMinutes(-1), 0),
            (SlotOfWeek38, 1),
            (SlotOfWeek38.AddHours(1), 1),
            (SlotOfWeek38.AddDays(3), 1),
            (SlotOfWeek38.AddDays(6).AddHours(9), 1),
            (SlotOfWeek39.AddMinutes(-1), 1),
            (SlotOfWeek39, 2),
            (SlotOfWeek39.AddHours(1), 2),
        ];
        foreach (var (time, digests) in ticks)
        {
            using var _ = Api.Clock.Set(time);
            await RunTick(hogs);
            PostedAfterTheHello(webhook).Count.ShouldBe(digests, $"after the tick at {time:u}");
        }

        (await FindDigest(hogs, Week38)).ShouldNotBeNull().Status.ShouldBe(NotificationStatus.Sent);
        (await FindDigest(hogs, Week39)).ShouldNotBeNull().Status.ShouldBe(NotificationStatus.Sent);
    }

    [Fact]
    public async Task A_tick_before_the_slot_decides_nothing()
    {
        var hogs = await OpenSeason();
        var webhook = await Connect(hogs, postDigest: true);

        using var _ = Api.Clock.Set(SlotOfWeek38.AddMinutes(-1));
        var summary = await RunTick(hogs);

        PostedAfterTheHello(webhook).ShouldBeEmpty();
        summary.DigestsPosted.ShouldBe(0);
        (await FindDigest(hogs, Week38)).ShouldBeNull();
    }

    [Fact]
    public async Task A_late_tick_still_posts_the_week_the_first_tick_missed()
    {
        var hogs = await OpenSeason();
        var webhook = await Connect(hogs, postDigest: true);

        using var _ = Api.Clock.Set(SlotOfWeek38.AddDays(3).AddHours(5));
        var summary = await RunTick(hogs);

        PostedAfterTheHello(webhook).ShouldHaveSingleItem();
        summary.DigestsPosted.ShouldBe(1);
        (await FindDigest(hogs, Week38)).ShouldNotBeNull().Status.ShouldBe(NotificationStatus.Sent);
    }

    [Fact]
    public async Task A_tick_that_finds_another_running_does_not_post_the_digest_twice()
    {
        var hogs = await OpenSeason();
        var webhook = await Connect(hogs, postDigest: true);

        using var _ = Api.Clock.Set(SlotOfWeek38);
        await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Api.Services.GetRequiredService<Tick>().RunAsync()));

        PostedAfterTheHello(webhook).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_digest_says_who_still_owes_the_pot_and_the_payments_waiting_on_the_treasurer()
    {
        var hogs = await OpenSeason();
        var desk = new TreasurersDesk(Api, hogs);
        var webhook = await Connect(hogs, postDigest: true);
        var paid = await desk.Attest(hogs.Sam, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1");
        await Quiesced(() => desk.Confirm(hogs.Sams, paid));
        await Quiesced(() => desk.Attest(hogs.Jacob, hogs.Jacobs, 30m, PaymentRail.Cash, null));

        using var _ = Api.Clock.Set(SlotOfWeek38);
        await RunTick(hogs);

        // Sam's confirmed payment settled the dues and is in the pot. Jacob's is not counted until the treasurer confirms it.
        PostedAfterTheHello(webhook).ShouldBe(
        [
            NotificationTexts.Digest(
                "Holland Hogs", "2026", [new("Hog Wild", 50m), new("Priya", 50m), new("Sam's Slammers", 0m), new("Team 4", 50m)], pot: 50m, attestationsPending: 1),
        ]);
    }

    [Fact]
    public async Task Nothing_is_posted_while_the_digest_is_off()
    {
        var hogs = await OpenSeason();
        var webhook = await Connect(hogs, postDigest: false);

        using var _ = Api.Clock.Set(SlotOfWeek38);
        var summary = await RunTick(hogs);

        PostedAfterTheHello(webhook).ShouldBeEmpty();
        summary.DigestsPosted.ShouldBe(0);
        (await FindDigest(hogs, Week38)).ShouldBeNull();
    }

    [Fact]
    public async Task Nothing_is_posted_once_Discord_is_disconnected()
    {
        var hogs = await OpenSeason();
        var webhook = await Connect(hogs, postDigest: true);
        (await Api.CreateClientFor(hogs.Jacob).DeleteAsync(SettingsPath(hogs))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var attempts = webhook.Attempts;

        using var _ = Api.Clock.Set(SlotOfWeek38);
        var summary = await RunTick(hogs);

        PostedAfterTheHello(webhook).ShouldBeEmpty();
        webhook.Attempts.ShouldBe(attempts);
        summary.DigestsPosted.ShouldBe(0);
        (await FindDigest(hogs, Week38)).ShouldBeNull();
    }

    [Fact]
    public async Task A_digest_the_channel_refuses_stays_pending_and_the_next_tick_tries_again()
    {
        var hogs = await OpenSeason();
        var webhook = await Connect(hogs, postDigest: true);
        webhook.RefusesWith(HttpStatusCode.InternalServerError);

        using (Api.Clock.Set(SlotOfWeek38))
        {
            var refused = await RunTick(hogs);

            refused.Failed.ShouldBe(1);
            refused.DigestsPosted.ShouldBe(0);
            (await FindDigest(hogs, Week38)).ShouldNotBeNull().Status.ShouldBe(NotificationStatus.Pending);
            PostedAfterTheHello(webhook).ShouldBeEmpty();
        }

        webhook.Accepts();
        using (Api.Clock.Set(SlotOfWeek38.AddHours(1)))
        {
            var retried = await RunTick(hogs);

            retried.DigestsPosted.ShouldBe(1);
            retried.Failed.ShouldBe(0);
            (await FindDigest(hogs, Week38)).ShouldNotBeNull().Status.ShouldBe(NotificationStatus.Sent);
            PostedAfterTheHello(webhook).ShouldHaveSingleItem();
        }

        using (Api.Clock.Set(SlotOfWeek38.AddHours(2)))
        {
            (await RunTick(hogs)).DigestsPosted.ShouldBe(0);
            PostedAfterTheHello(webhook).ShouldHaveSingleItem();
        }
    }

    [Fact]
    public async Task The_tick_does_not_succeed_while_the_digest_is_refused()
    {
        var hogs = await OpenSeason();
        var webhook = await Connect(hogs, postDigest: true);
        webhook.RefusesWith(HttpStatusCode.InternalServerError);

        using var _ = Api.Clock.Set(SlotOfWeek38);
        (await Api.Services.GetRequiredService<Tick>().RunAsync(hogs.LeagueId)).Succeeded.ShouldBeFalse();

        webhook.Accepts();
        (await Api.Services.GetRequiredService<Tick>().RunAsync(hogs.LeagueId)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task A_refused_digest_goes_with_what_is_owed_when_it_goes_not_when_it_was_decided()
    {
        var hogs = await OpenSeason();
        var desk = new TreasurersDesk(Api, hogs);
        var webhook = await Connect(hogs, postDigest: true);
        webhook.RefusesWith(HttpStatusCode.InternalServerError);
        using (Api.Clock.Set(SlotOfWeek38))
        {
            (await RunTick(hogs)).Failed.ShouldBe(1);
        }

        var payment = await desk.Attest(hogs.Sam, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1");
        await Quiesced(() => desk.Confirm(hogs.Sams, payment));
        webhook.Accepts();
        using var _ = Api.Clock.Set(SlotOfWeek38.AddHours(1));
        await RunTick(hogs);

        PostedAfterTheHello(webhook).ShouldBe(
        [
            NotificationTexts.Digest(
                "Holland Hogs", "2026", [new("Hog Wild", 50m), new("Priya", 50m), new("Sam's Slammers", 0m), new("Team 4", 50m)], pot: 50m, attestationsPending: 0),
        ]);
    }

    [Fact]
    public async Task A_digest_still_refused_when_its_week_is_over_is_skipped_and_the_next_week_has_its_own()
    {
        var hogs = await OpenSeason();
        var webhook = await Connect(hogs, postDigest: true);
        webhook.RefusesWith(HttpStatusCode.InternalServerError);
        using (Api.Clock.Set(SlotOfWeek38))
        {
            (await RunTick(hogs)).Failed.ShouldBe(1);
        }

        webhook.Accepts();
        using var _ = Api.Clock.Set(SlotOfWeek39);
        var summary = await RunTick(hogs);

        var missed = (await FindDigest(hogs, Week38)).ShouldNotBeNull();
        missed.Status.ShouldBe(NotificationStatus.Skipped);
        missed.Reason.ShouldBe(Digests.NoLongerDue);
        (await FindDigest(hogs, Week39)).ShouldNotBeNull().Status.ShouldBe(NotificationStatus.Sent);
        PostedAfterTheHello(webhook).ShouldHaveSingleItem();
        summary.DigestsPosted.ShouldBe(1);
        summary.Skipped.ShouldBe(1);
        summary.Failed.ShouldBe(0);
    }

    [Fact]
    public async Task A_refused_digest_is_not_sent_once_the_league_has_turned_the_digest_off()
    {
        var hogs = await OpenSeason();
        var webhook = await Connect(hogs, postDigest: true);
        webhook.RefusesWith(HttpStatusCode.InternalServerError);
        using (Api.Clock.Set(SlotOfWeek38))
        {
            (await RunTick(hogs)).Failed.ShouldBe(1);
        }

        webhook.Accepts();

        // Flags only: no URL, so no second hello.
        (await Api.CreateClientFor(hogs.Jacob).PutAsJsonAsync(SettingsPath(hogs), new DiscordSettingsRequest(null, AnnouncePayments: false, PostDigest: false)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        using var _ = Api.Clock.Set(SlotOfWeek38.AddHours(1));
        var summary = await RunTick(hogs);

        (await FindDigest(hogs, Week38)).ShouldNotBeNull().Status.ShouldBe(NotificationStatus.Skipped);
        PostedAfterTheHello(webhook).ShouldBeEmpty();
        summary.Skipped.ShouldBe(1);
        summary.DigestsPosted.ShouldBe(0);
    }

    [Fact]
    public async Task One_leagues_digest_goes_to_its_own_channel_and_not_to_another()
    {
        var hogs = await OpenSeason();
        var other = await OpenSeason();
        var hogsWebhook = await Connect(hogs, postDigest: true);
        var otherWebhook = await Connect(other, postDigest: false);

        using var _ = Api.Clock.Set(SlotOfWeek38);
        await RunTick(hogs);

        PostedAfterTheHello(hogsWebhook).ShouldHaveSingleItem();
        PostedAfterTheHello(otherWebhook).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_tick_says_in_its_log_line_that_it_posted_a_digest()
    {
        var hogs = await OpenSeason();
        await Connect(hogs, postDigest: true);

        using var _ = Api.Clock.Set(SlotOfWeek38);
        var summary = await RunTick(hogs);

        var line = Api.Logs.All
            .Where(entry => entry.Category == typeof(Tick).FullName && entry.Scope.Values.Any(value => hogs.LeagueId.ToString().Equals(value)))
            .Select(entry => entry.Message)
            .ShouldHaveSingleItem();
        line.ShouldContain($"sent {summary.Sent}, held {summary.Held}, skipped {summary.Skipped}, failed {summary.Failed}, digests 1, purged ");
    }

    private static string SettingsPath(HollandHogsSeason hogs) => $"/leagues/{hogs.LeagueId}/notifications/discord";

    // What the channel was told after the hello that connecting it posts.
    private static IReadOnlyList<string> PostedAfterTheHello(FakeDiscord.Webhook webhook) =>
        [.. webhook.Posted.Skip(1).Select(post => post.Content)];

    // Opening the season tells each member of the dues assessed to them; those messages are settled before a test goes on.
    private async Task<HollandHogsSeason> OpenSeason()
    {
        HollandHogsSeason? hogs = null;
        await Quiesced(async () => hogs = await HollandHogsSeason.Open(Api));
        return hogs!;
    }

    // Connects Discord as the treasurer does, and waits for the hello it posts.
    private async Task<FakeDiscord.Webhook> Connect(HollandHogsSeason hogs, bool postDigest)
    {
        var webhook = Api.Discord.NewWebhook();
        (await Api.CreateClientFor(hogs.Jacob).PutAsJsonAsync(SettingsPath(hogs), new DiscordSettingsRequest(webhook.Url, AnnouncePayments: false, postDigest)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        await webhook.WaitForPosts(1);
        return webhook;
    }

    private async Task<Notification?> FindDigest(HollandHogsSeason hogs, IsoWeek week)
    {
        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        return await session.LoadAsync<Notification>(Digests.Key(hogs.LeagueId, week));
    }

    // The tick runs every league in the test database, as the Job does; this league's part of the report is the one a test reads.
    private async Task<TickSummary> RunTick(HollandHogsSeason hogs)
    {
        var report = await Api.Services.GetRequiredService<Tick>().RunAsync();
        var failures = Api.Logs.All.Where(entry => entry.Message.Contains("did not finish") && entry.Message.Contains(hogs.LeagueId.ToString())).Select(entry => entry.Message);
        return report.Leagues.SingleOrDefault(summary => summary.LeagueId == hogs.LeagueId)
            ?? throw new InvalidOperationException($"The tick did not finish the league: {string.Join("; ", failures)}");
    }

    // Runs the action and waits until every message it caused, and every message those caused, has been handled.
    private async Task<ITrackedSession> Quiesced(Func<Task> action) =>
        await Api.Services.GetRequiredService<IHost>()
            .TrackActivity()
            .Timeout(TimeSpan.FromSeconds(30))
            .DoNotAssertOnExceptionsDetected()
            .ExecuteAndWaitAsync(_ => action());
}

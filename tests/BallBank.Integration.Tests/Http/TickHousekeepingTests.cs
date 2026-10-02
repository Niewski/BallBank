using BallBank.Api;
using BallBank.Api.Features.Notifications;
using BallBank.Domain.Notifications;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Wolverine.Tracking;

namespace BallBank.Integration.Tests.Http;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class TickHousekeepingTests(PostgresFixture postgres)
{
    // A Wednesday morning, Eastern, long before the 1 October due date, with Discord not connected: what a tick reports is its purge.
    private static readonly DateTimeOffset Now = new(2026, 6, 17, 14, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan DefaultAge = TimeSpan.FromDays(90);

    private BallBankApi Api => postgres.Api;

    private IDocumentStore Store => Api.Services.GetRequiredService<IDocumentStore>();

    [Fact]
    public async Task What_a_league_kept_from_opening_its_season_is_purged_once_it_is_older_than_the_age()
    {
        var hogs = await OpenSeasonAt(Now);
        var kept = await Kept(hogs);
        kept.IdempotencyRecords.ShouldNotBeEmpty();
        kept.Notifications.ShouldNotBeEmpty();

        using var _ = Api.Clock.Set(Now + DefaultAge + TimeSpan.FromMinutes(1));
        var summary = await Tick(hogs);

        summary.ShouldBe(new TickSummary(
            hogs.LeagueId, "2026", 0, 0, 0, 0, PurgedIdempotencyRecords: kept.IdempotencyRecords.Count, PurgedNotifications: kept.Notifications.Count));
        var left = await Kept(hogs);
        left.IdempotencyRecords.ShouldBeEmpty();
        left.Notifications.ShouldBeEmpty();
    }

    [Fact]
    public async Task What_is_not_yet_older_than_the_age_stays()
    {
        var hogs = await OpenSeasonAt(Now);
        var kept = await Kept(hogs);

        using var _ = Api.Clock.Set(Now + DefaultAge);
        var summary = await Tick(hogs);

        summary.PurgedIdempotencyRecords.ShouldBe(0);
        summary.PurgedNotifications.ShouldBe(0);
        var left = await Kept(hogs);
        left.IdempotencyRecords.ShouldBe(kept.IdempotencyRecords, ignoreOrder: true);
        left.Notifications.ShouldBe(kept.Notifications, ignoreOrder: true);
    }

    [Fact]
    public async Task A_record_is_purged_only_once_it_is_older_than_the_age_to_the_second()
    {
        var hogs = await OpenSeasonAt(Now);
        var cutoff = Now - DefaultAge;
        var pastIt = await Seed(hogs, cutoff.AddSeconds(-1));
        var exactlyAsOld = await Seed(hogs, cutoff);
        var insideIt = await Seed(hogs, cutoff.AddSeconds(1));

        using var _ = Api.Clock.Set(Now);
        var summary = await Tick(hogs);

        (await Exists(hogs, pastIt)).ShouldBe((false, false));
        (await Exists(hogs, exactlyAsOld)).ShouldBe((true, true));
        (await Exists(hogs, insideIt)).ShouldBe((true, true));
        summary.PurgedIdempotencyRecords.ShouldBe(1);
        summary.PurgedNotifications.ShouldBe(1);
    }

    [Fact]
    public async Task A_notification_is_purged_whatever_became_of_it()
    {
        var hogs = await OpenSeasonAt(Now);
        string[] statuses =
        [
            NotificationStatus.Pending, NotificationStatus.Sent, NotificationStatus.Dropped, NotificationStatus.Skipped,
            NotificationStatus.Delivered, NotificationStatus.Undelivered, NotificationStatus.Failed,
        ];
        var old = new List<string>();
        foreach (var status in statuses)
        {
            old.Add(await SeedNotification(hogs, Now.AddDays(-100), status));
        }

        using var _ = Api.Clock.Set(Now);
        var summary = await Tick(hogs);

        summary.PurgedNotifications.ShouldBe(statuses.Length);
        (await Kept(hogs)).Notifications.Intersect(old).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_age_is_configuration()
    {
        var hogs = await OpenSeasonAt(Now);
        var thirtyOneDaysOld = await Seed(hogs, Now.AddDays(-31));
        var twentyNineDaysOld = await Seed(hogs, Now.AddDays(-29));

        using var _ = Api.Clock.Set(Now);
        var byDefault = await Tick(hogs);

        byDefault.PurgedIdempotencyRecords.ShouldBe(0);
        byDefault.PurgedNotifications.ShouldBe(0);
        (await Exists(hogs, thirtyOneDaysOld)).ShouldBe((true, true));

        var thirtyDays = await Tick(hogs, retentionDays: 30);

        thirtyDays.PurgedIdempotencyRecords.ShouldBe(1);
        thirtyDays.PurgedNotifications.ShouldBe(1);
        (await Exists(hogs, thirtyOneDaysOld)).ShouldBe((false, false));
        (await Exists(hogs, twentyNineDaysOld)).ShouldBe((true, true));
    }

    [Fact]
    public async Task One_leagues_tick_purges_that_leagues_records_and_not_another_leagues()
    {
        var hogs = await OpenSeasonAt(Now);
        var other = await OpenSeasonAt(Now);
        var hogsOld = await Seed(hogs, Now.AddDays(-91));
        var otherOld = await Seed(other, Now.AddDays(-91));

        using var _ = Api.Clock.Set(Now);
        await Tick(hogs);

        (await Exists(hogs, hogsOld)).ShouldBe((false, false));
        (await Exists(other, otherOld)).ShouldBe((true, true));

        await Tick(other);

        (await Exists(other, otherOld)).ShouldBe((false, false));
    }

    [Fact]
    public async Task A_second_tick_has_nothing_left_to_purge()
    {
        var hogs = await OpenSeasonAt(Now);
        await Seed(hogs, Now.AddDays(-91));

        using var _ = Api.Clock.Set(Now);
        (await Tick(hogs)).PurgedNotifications.ShouldBe(1);

        var second = await Tick(hogs);

        second.PurgedIdempotencyRecords.ShouldBe(0);
        second.PurgedNotifications.ShouldBe(0);
    }

    [Fact]
    public async Task The_tick_says_in_its_log_line_what_it_purged()
    {
        var hogs = await OpenSeasonAt(Now);
        await Seed(hogs, Now.AddDays(-91));
        await Seed(hogs, Now.AddDays(-92));

        using var _ = Api.Clock.Set(Now);
        await Tick(hogs);

        var line = Api.Logs.All
            .Where(entry => entry.Category == typeof(Tick).FullName && entry.Scope.Values.Any(value => hogs.LeagueId.ToString().Equals(value)))
            .Select(entry => entry.Message)
            .ShouldHaveSingleItem();
        line.ShouldContain("digests 0, purged 2 idempotency records and 2 notifications");
    }

    [Fact]
    public async Task A_tick_configured_with_an_age_shorter_than_the_floor_purges_nothing_and_fails_the_league()
    {
        var hogs = await OpenSeasonAt(Now);
        var old = await Seed(hogs, Now.AddDays(-91));

        // The tick's host is built and not started, so nothing checks the age at start: reading it is what refuses.
        var options = new ServiceCollection()
            .AddRetention(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Retention:Days"] = "13" }).Build())
            .BuildServiceProvider()
            .GetRequiredService<IOptions<RetentionOptions>>();
        var tick = ActivatorUtilities.CreateInstance<Tick>(Api.Services, options);

        using var _ = Api.Clock.Set(Now);
        var report = await tick.RunAsync(hogs.LeagueId);

        report.LeaguesFailed.ShouldBe(1);
        report.Leagues.ShouldBeEmpty();
        report.Succeeded.ShouldBeFalse();
        (await Exists(hogs, old)).ShouldBe((true, true));
        Api.Logs.All
            .Where(entry => entry.Message.Contains("did not finish") && entry.Message.Contains(hogs.LeagueId.ToString()))
            .ShouldHaveSingleItem();
    }

    // The tick for this league alone, as the Job's tick runs for every league: another league's records are not this test's to purge.
    private async Task<TickSummary> Tick(HollandHogsSeason hogs, int? retentionDays = null)
    {
        var tick = retentionDays is { } days
            ? ActivatorUtilities.CreateInstance<Tick>(Api.Services, Options.Create(new RetentionOptions { Days = days }))
            : Api.Services.GetRequiredService<Tick>();
        var report = await tick.RunAsync(hogs.LeagueId);
        report.LeaguesFailed.ShouldBe(0, string.Join("; ", Api.Logs.All.Where(entry => entry.Message.Contains("did not finish")).Select(entry => entry.Message)));
        return report.Leagues.ShouldHaveSingleItem();
    }

    // Opening the season texts each member the dues; none has opted in, so those are skipped, and settled before a test goes on.
    private async Task<HollandHogsSeason> OpenSeasonAt(DateTimeOffset now)
    {
        using var _ = Api.Clock.Set(now);
        HollandHogsSeason? hogs = null;
        await Quiesced(async () => hogs = await HollandHogsSeason.Open(Api));
        return hogs!;
    }

    // Runs the action and waits until every message it caused, and every message those caused, has been handled.
    private async Task<ITrackedSession> Quiesced(Func<Task> action) =>
        await Api.Services.GetRequiredService<IHost>()
            .TrackActivity()
            .Timeout(TimeSpan.FromSeconds(30))
            .DoNotAssertOnExceptionsDetected()
            .ExecuteAndWaitAsync(_ => action());

    // One answer kept for a retry and one record of a notification, both written as of the time.
    private async Task<Seeded> Seed(HollandHogsSeason hogs, DateTimeOffset when)
    {
        var idempotencyId = IdempotencyRecord.IdFor($"test|{Guid.NewGuid():N}", Guid.NewGuid().ToString());
        await using (var session = Store.LightweightSession(hogs.LeagueId.ToString()))
        {
            session.Insert(new IdempotencyRecord { Id = idempotencyId, Fingerprint = "seeded", Status = 201, Body = "{}", RecordedAt = when });
            await session.SaveChangesAsync();
        }

        return new Seeded(idempotencyId, await SeedNotification(hogs, when, NotificationStatus.Sent));
    }

    private async Task<string> SeedNotification(HollandHogsSeason hogs, DateTimeOffset when, string status)
    {
        var id = NotificationKey.For(NotificationKinds.PaymentConfirmed, Guid.NewGuid(), Channels.Discord, hogs.LeagueId);
        await using var session = Store.LightweightSession(hogs.LeagueId.ToString());
        session.Insert(new Notification
        {
            Id = id,
            Kind = NotificationKinds.PaymentConfirmed,
            Channel = Channels.Discord,
            Text = "A payment was confirmed.",
            Status = status,
            CreatedAt = when,
            SentAt = status == NotificationStatus.Pending ? null : when,
        });
        await session.SaveChangesAsync();
        return id;
    }

    private async Task<(bool IdempotencyRecord, bool Notification)> Exists(HollandHogsSeason hogs, Seeded seeded)
    {
        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        return (
            await session.LoadAsync<IdempotencyRecord>(seeded.IdempotencyId) is not null,
            await session.LoadAsync<Notification>(seeded.NotificationId) is not null);
    }

    private async Task<(IReadOnlyList<string> IdempotencyRecords, IReadOnlyList<string> Notifications)> Kept(HollandHogsSeason hogs)
    {
        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        var records = await session.Query<IdempotencyRecord>().ToListAsync();
        var notifications = await session.Query<Notification>().ToListAsync();
        return ([.. records.Select(record => record.Id)], [.. notifications.Select(notification => notification.Id)]);
    }

    private sealed record Seeded(string IdempotencyId, string NotificationId);
}

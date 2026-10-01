using System.Net;
using System.Net.Http.Json;
using BallBank.Api.Features.Membership;
using BallBank.Api.Features.Notifications;
using BallBank.Api.Features.Treasury;
using BallBank.Domain.Notifications;
using BallBank.Domain.Treasury;
using BallBank.Integration.Tests.Twilio;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine.Tracking;

namespace BallBank.Integration.Tests.Http;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class TickTests(PostgresFixture postgres)
{
    private const string SamsPhone = "+15550100002";

    // Mid-morning Eastern, outside the quiet hours a member has until they choose. The season's dues are due Thursday 1 October.
    private static readonly DateTimeOffset ThreeDaysBefore = At(2026, 9, 28);
    private static readonly DateTimeOffset OnTheDay = At(2026, 10, 1);

    private BallBankApi Api => postgres.Api;
    private FakeTwilio Twilio => Api.Twilio;

    [Fact]
    public async Task Three_days_before_the_due_date_a_member_who_owes_is_texted_a_reminder()
    {
        using var _ = Api.Clock.Set(ThreeDaysBefore);
        var hogs = await OpenSeason();
        await OptIn(hogs, hogs.Sam, hogs.Sams, "555 010 0002");

        var summary = await RunTick(hogs);

        var text = Twilio.SentTo(hogs.LeagueId, SamsPhone).ShouldHaveSingleItem();
        text.Body.ShouldBe(SmsTexts.Reminder("Holland Hogs", 50m, HollandHogsSeason.DueDate, new DateOnly(2026, 9, 28), StatementLink(hogs, hogs.Sams)));
        summary.Sent.ShouldBe(1);
        var reminder = await ReminderFor(hogs, hogs.Sams, ReminderStage.ThreeDaysBefore);
        reminder.Status.ShouldBe(NotificationStatus.Sent);
        reminder.Kind.ShouldBe(NotificationKinds.Reminder);
        reminder.AccountId.ShouldBe(hogs.AccountOf(hogs.Sams));
        reminder.MemberId.ShouldBe(hogs.Sams);
        reminder.SentAt.ShouldBe(ThreeDaysBefore);
    }

    [Fact]
    public async Task Nothing_is_sent_more_than_three_days_before_the_due_date()
    {
        using var _ = Api.Clock.Set(At(2026, 9, 27));
        var hogs = await OpenSeason();
        await OptIn(hogs, hogs.Sam, hogs.Sams, "555 010 0002");

        var summary = await RunTick(hogs);

        Twilio.Attempts(hogs.LeagueId).ShouldBe(0);
        summary.ShouldBe(new TickSummary(hogs.LeagueId, "2026", 0, 0, 0, 0));
    }

    [Fact]
    public async Task Running_the_tick_twice_sends_one_reminder()
    {
        using var _ = Api.Clock.Set(ThreeDaysBefore);
        var hogs = await OpenSeason();
        await OptIn(hogs, hogs.Sam, hogs.Sams, "555 010 0002");

        var first = await RunTick(hogs);
        var second = await RunTick(hogs);

        Twilio.SentTo(hogs.LeagueId, SamsPhone).ShouldHaveSingleItem();
        Twilio.Attempts(hogs.LeagueId).ShouldBe(1);
        first.Sent.ShouldBe(1);
        second.ShouldBe(new TickSummary(hogs.LeagueId, "2026", 0, 0, 0, 0));
    }

    [Fact]
    public async Task Two_ticks_at_once_send_one_reminder()
    {
        using var _ = Api.Clock.Set(ThreeDaysBefore);
        var hogs = await OpenSeason();
        await OptIn(hogs, hogs.Sam, hogs.Sams, "555 010 0002");

        var reports = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Api.Services.GetRequiredService<Tick>().RunAsync()));

        // One of them may have found the other running, and done nothing; either way the text went once.
        Twilio.SentTo(hogs.LeagueId, SamsPhone).ShouldHaveSingleItem();
        Twilio.Attempts(hogs.LeagueId).ShouldBe(1);
        reports.Sum(report => report.Leagues.Count(summary => summary.LeagueId == hogs.LeagueId)).ShouldBeInRange(1, 2);
        reports.ShouldAllBe(report => report.LeaguesFailed == 0);
    }

    [Fact]
    public async Task Each_stage_is_sent_once_as_the_due_date_comes_and_goes()
    {
        var hogs = await OpenSeasonAt(ThreeDaysBefore);
        await OptInAt(ThreeDaysBefore, hogs);

        // Day by day: 3 days before, the day itself, then each whole week overdue. In between, the day's stage stands and is not repeated.
        (DateTimeOffset Time, int Texts)[] days =
        [
            (At(2026, 9, 28), 1),
            (At(2026, 9, 29), 1),
            (At(2026, 9, 30), 1),
            (At(2026, 10, 1), 2),
            (At(2026, 10, 2), 2),
            (At(2026, 10, 7), 2),
            (At(2026, 10, 8), 3),
            (At(2026, 10, 14), 3),
            (At(2026, 10, 15), 4),
        ];
        foreach (var (time, texts) in days)
        {
            using var _ = Api.Clock.Set(time);
            await RunTick(hogs);
            Twilio.SentTo(hogs.LeagueId, SamsPhone).Count.ShouldBe(texts, $"after the tick on {time:yyyy-MM-dd}");
        }

        var stages = (await RemindersFor(hogs, hogs.Sams)).Select(reminder => reminder.Id.Split('/').Last()).Order().ToList();
        stages.ShouldBe(["1-week-overdue", "2-weeks-overdue", "3-days-before", "on-the-day"]);
        Twilio.SentTo(hogs.LeagueId, SamsPhone).Last().Body.ShouldContain("14 days overdue");
    }

    [Fact]
    public async Task A_tick_that_was_missed_on_the_day_sends_that_days_reminder_at_the_next()
    {
        var hogs = await OpenSeasonAt(At(2026, 9, 28));
        await OptInAt(At(2026, 9, 28), hogs);

        using var _ = Api.Clock.Set(At(2026, 10, 3));
        await RunTick(hogs);

        var text = Twilio.SentTo(hogs.LeagueId, SamsPhone).ShouldHaveSingleItem();
        text.Body.ShouldBe(SmsTexts.Reminder("Holland Hogs", 50m, HollandHogsSeason.DueDate, new DateOnly(2026, 10, 3), StatementLink(hogs, hogs.Sams)));
        (await ReminderFor(hogs, hogs.Sams, ReminderStage.OnTheDay)).Status.ShouldBe(NotificationStatus.Sent);
    }

    [Fact]
    public async Task A_member_who_has_paid_up_is_not_reminded()
    {
        using var _ = Api.Clock.Set(OnTheDay);
        var hogs = await OpenSeason();
        var desk = new TreasurersDesk(Api, hogs);
        await OptIn(hogs, hogs.Sam, hogs.Sams, "555 010 0002");
        var payment = await desk.Attest(hogs.Sam, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1");
        await Quiesced(() => desk.Confirm(hogs.Sams, payment));
        await Twilio.WaitForTexts(hogs.LeagueId, 1);

        var summary = await RunTick(hogs);

        // The one text is the confirmation; no reminder was decided for Sam at all. The members who owe, and are not opted in, are skipped.
        Twilio.SentTo(hogs.LeagueId, SamsPhone).ShouldHaveSingleItem().Body.ShouldNotContain("You owe");
        (await RemindersFor(hogs, hogs.Sams)).ShouldBeEmpty();
        summary.Sent.ShouldBe(0);
    }

    [Fact]
    public async Task A_payment_still_waiting_for_the_treasurer_does_not_stop_the_reminders()
    {
        using var _ = Api.Clock.Set(OnTheDay);
        var hogs = await OpenSeason();
        var desk = new TreasurersDesk(Api, hogs);
        await OptIn(hogs, hogs.Sam, hogs.Sams, "555 010 0002");
        await Quiesced(() => desk.Attest(hogs.Sam, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1"));

        await RunTick(hogs);

        // Only a confirmed payment counts against what is owed (the statement's balance), as on the dashboard.
        (await ReminderFor(hogs, hogs.Sams, ReminderStage.OnTheDay)).Status.ShouldBe(NotificationStatus.Sent);
    }

    [Fact]
    public async Task A_member_who_has_not_opted_in_is_not_texted_and_the_reminder_is_skipped()
    {
        using var _ = Api.Clock.Set(ThreeDaysBefore);
        var hogs = await OpenSeason();

        var summary = await RunTick(hogs);

        var reminder = await ReminderFor(hogs, hogs.Sams, ReminderStage.ThreeDaysBefore);
        reminder.Status.ShouldBe(NotificationStatus.Skipped);
        reminder.Reason.ShouldBe(SmsDelivery.NoConsent);
        Twilio.Attempts(hogs.LeagueId).ShouldBe(0);
        summary.Skipped.ShouldBeGreaterThan(0);
        summary.Sent.ShouldBe(0);
    }

    [Fact]
    public async Task A_reminder_in_the_members_quiet_hours_is_held_and_released_by_the_tick_after_they_end()
    {
        // 11pm Sunday in Eastern, in the quiet hours a member has until they choose; they end at 9am.
        var night = new DateTimeOffset(2026, 9, 28, 3, 0, 0, TimeSpan.Zero);
        var hogs = await OpenSeasonAt(ThreeDaysBefore);
        await OptInAt(ThreeDaysBefore, hogs);

        using (Api.Clock.Set(night))
        {
            var held = await RunTick(hogs);

            held.Held.ShouldBe(1);
            held.Sent.ShouldBe(0);
            var reminder = await ReminderFor(hogs, hogs.Sams, ReminderStage.ThreeDaysBefore);
            reminder.Status.ShouldBe(NotificationStatus.Held);
            reminder.Reason.ShouldBe(SmsDelivery.InQuietHours);
            reminder.SendAfter.ShouldBe(new DateTimeOffset(2026, 9, 28, 13, 0, 0, TimeSpan.Zero));
            Twilio.Attempts(hogs.LeagueId).ShouldBe(0);
        }

        // Still the quiet hours: held, and not yet its time.
        using (Api.Clock.Set(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero)))
        {
            (await RunTick(hogs)).ShouldBe(new TickSummary(hogs.LeagueId, "2026", 0, 0, 0, 0));
            Twilio.Attempts(hogs.LeagueId).ShouldBe(0);
        }

        using (Api.Clock.Set(ThreeDaysBefore))
        {
            var released = await RunTick(hogs);

            released.Sent.ShouldBe(1);
            (await ReminderFor(hogs, hogs.Sams, ReminderStage.ThreeDaysBefore)).Status.ShouldBe(NotificationStatus.Sent);
            Twilio.SentTo(hogs.LeagueId, SamsPhone).ShouldHaveSingleItem();
        }
    }

    [Fact]
    public async Task A_held_reminder_is_not_sent_once_the_member_has_paid_up()
    {
        var night = new DateTimeOffset(2026, 9, 28, 3, 0, 0, TimeSpan.Zero);
        var hogs = await OpenSeasonAt(ThreeDaysBefore);
        var desk = new TreasurersDesk(Api, hogs);
        await OptInAt(ThreeDaysBefore, hogs);
        using (Api.Clock.Set(night))
        {
            (await RunTick(hogs)).Held.ShouldBe(1);
            var payment = await desk.Attest(hogs.Sam, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1");
            await Quiesced(() => desk.Confirm(hogs.Sams, payment));
        }

        using var _ = Api.Clock.Set(ThreeDaysBefore);
        var summary = await RunTick(hogs);

        var reminder = await ReminderFor(hogs, hogs.Sams, ReminderStage.ThreeDaysBefore);
        reminder.Status.ShouldBe(NotificationStatus.Skipped);
        reminder.Reason.ShouldBe(Reminders.PaidUp);
        Twilio.SentTo(hogs.LeagueId, SamsPhone).ShouldHaveSingleItem().Body.ShouldNotContain("You owe");
        summary.Skipped.ShouldBe(1);
    }

    [Fact]
    public async Task A_held_reminder_names_what_the_member_owes_when_it_goes_not_when_it_was_held()
    {
        var night = new DateTimeOffset(2026, 9, 28, 3, 0, 0, TimeSpan.Zero);
        var hogs = await OpenSeasonAt(ThreeDaysBefore);
        var desk = new TreasurersDesk(Api, hogs);
        await OptInAt(ThreeDaysBefore, hogs);
        using (Api.Clock.Set(night))
        {
            (await RunTick(hogs)).Held.ShouldBe(1);
            var payment = await desk.Attest(hogs.Sam, hogs.Sams, 20m, PaymentRail.Venmo, "VN-1");
            await Quiesced(() => desk.Confirm(hogs.Sams, payment));
        }

        using var _ = Api.Clock.Set(ThreeDaysBefore);
        await RunTick(hogs);

        var bodies = Twilio.SentTo(hogs.LeagueId, SamsPhone).Select(text => text.Body).ToList();
        bodies.ShouldContain(SmsTexts.Reminder("Holland Hogs", 30m, HollandHogsSeason.DueDate, new DateOnly(2026, 9, 28), StatementLink(hogs, hogs.Sams)));
        bodies.ShouldNotContain(body => body.Contains("$50.00"));
    }

    [Fact]
    public async Task The_tick_does_not_succeed_while_a_send_is_refused()
    {
        using var _ = Api.Clock.Set(ThreeDaysBefore);
        var hogs = await OpenSeason();
        await OptIn(hogs, hogs.Sam, hogs.Sams, "555 010 0002");
        Twilio.RefusesWith(hogs.LeagueId, HttpStatusCode.InternalServerError);

        var refused = await Api.Services.GetRequiredService<Tick>().RunAsync(hogs.LeagueId);

        refused.Succeeded.ShouldBeFalse();

        Twilio.Accepts(hogs.LeagueId);
        (await Api.Services.GetRequiredService<Tick>().RunAsync(hogs.LeagueId)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task The_tick_releases_a_held_confirmation_when_the_quiet_hours_end()
    {
        var night = new DateTimeOffset(2026, 9, 28, 3, 0, 0, TimeSpan.Zero);
        var hogs = await OpenSeasonAt(ThreeDaysBefore);
        var desk = new TreasurersDesk(Api, hogs);
        await OptInAt(ThreeDaysBefore, hogs);
        Guid payment;
        using (Api.Clock.Set(night))
        {
            payment = await desk.Attest(hogs.Sam, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1");
            await Quiesced(() => desk.Confirm(hogs.Sams, payment));
            Twilio.Attempts(hogs.LeagueId).ShouldBe(0);
        }

        using var _ = Api.Clock.Set(ThreeDaysBefore);
        var summary = await RunTick(hogs);

        var text = Twilio.SentTo(hogs.LeagueId, SamsPhone).ShouldHaveSingleItem();
        text.Body.ShouldBe(SmsTexts.PaymentConfirmed("Holland Hogs", 50m, PaymentRail.Venmo, StatementLink(hogs, hogs.Sams)));
        summary.Sent.ShouldBe(1);
    }

    [Fact]
    public async Task A_reminder_the_channel_refuses_stays_pending_and_the_next_tick_tries_again()
    {
        using var _ = Api.Clock.Set(ThreeDaysBefore);
        var hogs = await OpenSeason();
        await OptIn(hogs, hogs.Sam, hogs.Sams, "555 010 0002");
        Twilio.RefusesWith(hogs.LeagueId, HttpStatusCode.InternalServerError);

        var refused = await RunTick(hogs);

        refused.Failed.ShouldBe(1);
        refused.Sent.ShouldBe(0);
        (await ReminderFor(hogs, hogs.Sams, ReminderStage.ThreeDaysBefore)).Status.ShouldBe(NotificationStatus.Pending);
        Twilio.Sent(hogs.LeagueId).ShouldBeEmpty();

        Twilio.Accepts(hogs.LeagueId);
        var retried = await RunTick(hogs);

        retried.Sent.ShouldBe(1);
        retried.Failed.ShouldBe(0);
        (await ReminderFor(hogs, hogs.Sams, ReminderStage.ThreeDaysBefore)).Status.ShouldBe(NotificationStatus.Sent);
        Twilio.SentTo(hogs.LeagueId, SamsPhone).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_tick_says_in_one_log_line_per_league_what_it_sent_held_and_skipped()
    {
        using var _ = Api.Clock.Set(ThreeDaysBefore);
        var hogs = await OpenSeason();
        await OptIn(hogs, hogs.Sam, hogs.Sams, "555 010 0002");

        var summary = await RunTick(hogs);

        var lines = Api.Logs.All
            .Where(entry => entry.Category == typeof(Tick).FullName && entry.Scope.Values.Any(value => hogs.LeagueId.ToString().Equals(value)))
            .Select(entry => entry.Message)
            .ToList();
        var line = lines.ShouldHaveSingleItem();
        line.ShouldContain(hogs.LeagueId.ToString());
        line.ShouldContain("season 2026");
        line.ShouldContain($"sent {summary.Sent}, held {summary.Held}, skipped {summary.Skipped}, failed {summary.Failed}");
        summary.Sent.ShouldBe(1);
        summary.Skipped.ShouldBeGreaterThan(0);
        Api.Logs.All.Where(entry => entry.Message.Contains(SamsPhone) || entry.Message.Contains("5550100002")).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_league_without_an_open_season_is_not_ticked()
    {
        using var _ = Api.Clock.Set(ThreeDaysBefore);
        var jacob = $"test|{Guid.NewGuid():N}";
        var leagueId = Guid.NewGuid();
        (await Api.CreateClientFor(jacob).PostAsJsonAsync(
            $"/leagues/{leagueId}/import",
            new ImportLeagueRequest(Api.Sleeper.CopyOfHollandHogs(), "jacob", "Jacob"))).StatusCode.ShouldBe(HttpStatusCode.Created);

        var report = await Api.Services.GetRequiredService<Tick>().RunAsync();

        report.Leagues.ShouldNotContain(summary => summary.LeagueId == leagueId);
    }

    [Fact]
    public async Task One_leagues_tick_does_not_touch_anothers()
    {
        using var _ = Api.Clock.Set(ThreeDaysBefore);
        var hogs = await OpenSeason();
        var other = await OpenSeason();
        await OptIn(hogs, hogs.Sam, hogs.Sams, "555 010 0002");
        await OptIn(other, other.Sam, other.Sams, "555 010 0002");

        var report = await Api.Services.GetRequiredService<Tick>().RunAsync();

        report.Leagues.Count(summary => summary.LeagueId == hogs.LeagueId || summary.LeagueId == other.LeagueId).ShouldBe(2);
        Twilio.SentTo(hogs.LeagueId, SamsPhone).ShouldHaveSingleItem();
        Twilio.SentTo(other.LeagueId, SamsPhone).ShouldHaveSingleItem();
        (await ReminderFor(hogs, hogs.Sams, ReminderStage.ThreeDaysBefore)).Text.ShouldContain(StatementLink(hogs, hogs.Sams));
        (await ReminderFor(other, other.Sams, ReminderStage.ThreeDaysBefore)).Text.ShouldContain(StatementLink(other, other.Sams));
    }

    [Fact]
    public async Task A_tick_asked_for_one_league_leaves_the_others_to_the_next_tick()
    {
        using var _ = Api.Clock.Set(ThreeDaysBefore);
        var hogs = await OpenSeason();
        var other = await OpenSeason();
        await OptIn(hogs, hogs.Sam, hogs.Sams, "555 010 0002");
        await OptIn(other, other.Sam, other.Sams, "555 010 0002");

        var report = await Api.Services.GetRequiredService<Tick>().RunAsync(onlyLeague: hogs.LeagueId);

        report.Leagues.Select(summary => summary.LeagueId).ShouldBe([hogs.LeagueId]);
        Twilio.SentTo(hogs.LeagueId, SamsPhone).ShouldHaveSingleItem();
        Twilio.SentTo(other.LeagueId, SamsPhone).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_dashboard_shows_each_delinquents_last_reminder_and_how_it_went()
    {
        var hogs = await OpenSeasonAt(ThreeDaysBefore);
        var desk = new TreasurersDesk(Api, hogs);
        await OptInAt(ThreeDaysBefore, hogs);
        foreach (var day in new[] { OnTheDay, At(2026, 10, 8) })
        {
            using var _ = Api.Clock.Set(day);
            await RunTick(hogs);
        }

        await desk.Settled();
        using var __ = Api.Clock.Set(At(2026, 10, 11));
        var dashboard = (await Api.CreateClientFor(hogs.Jacob).GetFromJsonAsync<Dashboard>($"/leagues/{hogs.LeagueId}/seasons/2026/dashboard")).ShouldNotBeNull();

        // Sam was texted twice, and the week's text is the last; Jacob never said yes to texts, so what was decided for him was skipped.
        dashboard.Delinquents.Single(d => d.MemberId == hogs.Sams).LastReminder
            .ShouldBe(new LastReminder(At(2026, 10, 8), NotificationStatus.Sent, Reason: null));
        dashboard.Delinquents.Single(d => d.MemberId == hogs.Jacobs).LastReminder
            .ShouldBe(new LastReminder(At(2026, 10, 8), NotificationStatus.Skipped, SmsDelivery.NoConsent));
    }

    [Fact]
    public async Task A_delinquent_who_was_never_reminded_has_no_last_reminder()
    {
        var hogs = await OpenSeasonAt(ThreeDaysBefore);
        var desk = new TreasurersDesk(Api, hogs);
        await desk.Settled();

        using var _ = Api.Clock.Set(At(2026, 10, 11));
        var dashboard = (await Api.CreateClientFor(hogs.Jacob).GetFromJsonAsync<Dashboard>($"/leagues/{hogs.LeagueId}/seasons/2026/dashboard")).ShouldNotBeNull();

        dashboard.Delinquents.ShouldNotBeEmpty();
        dashboard.Delinquents.ShouldAllBe(d => d.LastReminder == null);
    }

    private static DateTimeOffset At(int year, int month, int day) => new(year, month, day, 14, 0, 0, TimeSpan.Zero);

    private IDocumentStore Store => Api.Services.GetRequiredService<IDocumentStore>();

    private static string StatementLink(HollandHogsSeason hogs, Guid memberId) =>
        SmsTexts.StatementLink(BallBankApi.WebBaseUrl, hogs.LeagueId, hogs.AccountOf(memberId));

    // The tick runs every league in the test database, as the Job does; this league's part of the report is the one a test reads.
    private async Task<TickSummary> RunTick(HollandHogsSeason hogs)
    {
        var report = await Api.Services.GetRequiredService<Tick>().RunAsync();
        var failures = Api.Logs.All.Where(entry => entry.Message.Contains("did not finish") && entry.Message.Contains(hogs.LeagueId.ToString())).Select(entry => entry.Message);
        return report.Leagues.SingleOrDefault(summary => summary.LeagueId == hogs.LeagueId)
            ?? throw new InvalidOperationException($"The tick did not finish the league: {string.Join("; ", failures)}");
    }

    private async Task<Notification> ReminderFor(HollandHogsSeason hogs, Guid memberId, ReminderStage stage)
    {
        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        var key = Reminders.Key(Channels.Sms, memberId, hogs.AccountOf(memberId), HollandHogsSeason.DueDate, stage);
        return (await session.LoadAsync<Notification>(key)).ShouldNotBeNull();
    }

    private async Task<IReadOnlyList<Notification>> RemindersFor(HollandHogsSeason hogs, Guid memberId)
    {
        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        return await session.Query<Notification>()
            .Where(notification => notification.Kind == NotificationKinds.Reminder && notification.MemberId == memberId)
            .ToListAsync();
    }

    private async Task<HollandHogsSeason> OpenSeasonAt(DateTimeOffset now)
    {
        using var _ = Api.Clock.Set(now);
        return await OpenSeason();
    }

    private async Task OptInAt(DateTimeOffset now, HollandHogsSeason hogs)
    {
        using var _ = Api.Clock.Set(now);
        await OptIn(hogs, hogs.Sam, hogs.Sams, "555 010 0002");
    }

    // As a member does: the number on record, then consent to be texted at it.
    private async Task OptIn(HollandHogsSeason hogs, string subject, Guid memberId, string phone)
    {
        var client = Api.CreateClientFor(subject);
        (await client.PutAsJsonAsync($"/leagues/{hogs.LeagueId}/members/{memberId}/contact", new ContactDetailsRequest("member@example.com", phone)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PutAsJsonAsync($"/leagues/{hogs.LeagueId}/members/{memberId}/notifications", new NotificationPreferencesRequest(TextMe: true)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    // Opening the season tells each member of the dues assessed to them; no member has opted in yet, so those texts
    // are skipped, and they are settled before a test goes on to opt a member in.
    private async Task<HollandHogsSeason> OpenSeason()
    {
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
}

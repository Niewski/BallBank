using BallBank.Domain.Notifications;

namespace BallBank.Domain.Tests.Notifications;

public class DigestsTests
{
    private static readonly Guid League = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // Instants are written in UTC and read in Eastern: four hours behind until the clocks go back on Nov 1, 2026, five after.
    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void Nothing_is_due_before_nine_on_a_monday_morning_Eastern()
    {
        // Monday Sep 28, 2026: 08:59 in New York, an hour short of the slot.
        Digests.WeekDueAt(Utc(2026, 9, 28, 12, 59)).ShouldBeNull();
    }

    [Fact]
    public void Nothing_is_due_in_the_small_hours_of_a_monday_either_though_the_week_before_was_never_digested()
    {
        // Monday Sep 28, 00:30 in New York: a week missed entirely is not made up.
        Digests.WeekDueAt(Utc(2026, 9, 28, 4, 30)).ShouldBeNull();
    }

    [Fact]
    public void The_week_is_due_from_nine_on_the_monday_morning_Eastern()
    {
        Digests.WeekDueAt(Utc(2026, 9, 28, 13, 0)).ShouldBe(new IsoWeek(2026, 40));
    }

    [Fact]
    public void The_week_stays_due_to_its_end_so_a_late_tick_still_finds_it()
    {
        Digests.WeekDueAt(Utc(2026, 9, 29, 3, 0)).ShouldBe(new IsoWeek(2026, 40)); // Monday 23:00
        Digests.WeekDueAt(Utc(2026, 9, 30, 15, 0)).ShouldBe(new IsoWeek(2026, 40)); // Wednesday 11:00
        Digests.WeekDueAt(Utc(2026, 10, 4, 22, 0)).ShouldBe(new IsoWeek(2026, 40)); // Sunday 18:00, the wake-up slot
    }

    [Fact]
    public void Sunday_night_Eastern_belongs_to_the_week_ending_though_it_is_already_monday_in_UTC()
    {
        // Sunday Oct 4, 23:59 in New York is 03:59 on Monday, Oct 5, in UTC.
        Digests.WeekDueAt(Utc(2026, 10, 5, 3, 59)).ShouldBe(new IsoWeek(2026, 40));
    }

    [Fact]
    public void The_next_week_is_due_from_its_own_slot()
    {
        Digests.WeekDueAt(Utc(2026, 10, 5, 12, 59)).ShouldBeNull();
        Digests.WeekDueAt(Utc(2026, 10, 5, 13, 0)).ShouldBe(new IsoWeek(2026, 41));
    }

    [Fact]
    public void The_slot_is_nine_by_the_clocks_in_New_York_when_they_go_back()
    {
        // From Nov 1, 2026 New York is on standard time: nine in the morning is 14:00 UTC.
        Digests.WeekDueAt(Utc(2026, 11, 2, 13, 59)).ShouldBeNull();
        Digests.WeekDueAt(Utc(2026, 11, 2, 14, 0)).ShouldBe(new IsoWeek(2026, 45));
    }

    [Fact]
    public void The_slot_is_nine_by_the_clocks_in_New_York_when_they_go_forward()
    {
        // From Mar 8, 2026 New York is on daylight time: nine in the morning is 13:00 UTC.
        Digests.WeekDueAt(Utc(2026, 3, 9, 12, 59)).ShouldBeNull();
        Digests.WeekDueAt(Utc(2026, 3, 9, 13, 0)).ShouldBe(new IsoWeek(2026, 11));
    }

    [Fact]
    public void A_week_belongs_to_its_ISO_year_not_the_calendar_year()
    {
        // Monday Dec 29, 2025 starts the first week of 2026; Friday Jan 1, 2027 is in the last week of 2026, its 53rd.
        Digests.WeekDueAt(Utc(2025, 12, 29, 14, 0)).ShouldBe(new IsoWeek(2026, 1));
        Digests.WeekDueAt(Utc(2027, 1, 1, 15, 0)).ShouldBe(new IsoWeek(2026, 53));
    }

    [Fact]
    public void A_week_reads_as_its_year_and_its_number_to_two_digits()
    {
        new IsoWeek(2026, 5).ToString().ShouldBe("2026-W05");
        new IsoWeek(2026, 40).ToString().ShouldBe("2026-W40");
        new IsoWeek(2026, 53).ToString().ShouldBe("2026-W53");
    }

    [Fact]
    public void A_digest_is_keyed_on_the_league_and_the_week()
    {
        Digests.Key(League, new IsoWeek(2026, 40)).ShouldBe("Discord/11111111-1111-1111-1111-111111111111/Digest/2026-W40");
    }

    [Fact]
    public void Another_week_or_another_league_is_another_key()
    {
        var key = Digests.Key(League, new IsoWeek(2026, 40));

        Digests.Key(League, new IsoWeek(2026, 41)).ShouldNotBe(key);
        Digests.Key(League, new IsoWeek(2025, 40)).ShouldNotBe(key);
        Digests.Key(Guid.NewGuid(), new IsoWeek(2026, 40)).ShouldNotBe(key);
        Digests.Key(League, new IsoWeek(2026, 40)).ShouldBe(key);
    }
}

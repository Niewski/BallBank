using BallBank.Domain.Notifications;

namespace BallBank.Domain.Tests.Notifications;

public class QuietHoursTests
{
    private const string Eastern = "America/New_York";
    private const string Pacific = "America/Los_Angeles";

    private static readonly TimeSpan Edt = TimeSpan.FromHours(-4);
    private static readonly TimeSpan Est = TimeSpan.FromHours(-5);

    private static DateTimeOffset At(int year, int month, int day, int hour, int minute, TimeSpan offset) =>
        new(year, month, day, hour, minute, 0, offset);

    [Fact]
    public void Quiet_hours_are_nine_at_night_to_nine_in_the_morning_Eastern_until_chosen_otherwise()
    {
        QuietHours.Default.ShouldBe(new QuietHours(21, 9, Eastern));
    }

    [Fact]
    public void An_instant_outside_quiet_hours_is_not_held()
    {
        QuietHours.Default.HeldUntil(At(2026, 9, 29, 15, 0, Edt)).ShouldBeNull();
    }

    [Fact]
    public void An_instant_at_night_is_held_until_quiet_hours_end_the_next_morning()
    {
        QuietHours.Default.HeldUntil(At(2026, 9, 29, 23, 30, Edt)).ShouldBe(At(2026, 9, 30, 9, 0, Edt));
    }

    [Fact]
    public void An_instant_after_midnight_is_held_until_they_end_that_morning()
    {
        QuietHours.Default.HeldUntil(At(2026, 9, 30, 2, 0, Edt)).ShouldBe(At(2026, 9, 30, 9, 0, Edt));
    }

    [Fact]
    public void Quiet_hours_begin_on_the_start_hour_and_end_on_the_end_hour()
    {
        QuietHours.Default.HeldUntil(At(2026, 9, 29, 21, 0, Edt)).ShouldBe(At(2026, 9, 30, 9, 0, Edt));
        QuietHours.Default.HeldUntil(At(2026, 9, 29, 20, 59, Edt)).ShouldBeNull();
        QuietHours.Default.HeldUntil(At(2026, 9, 30, 8, 59, Edt)).ShouldBe(At(2026, 9, 30, 9, 0, Edt));
        QuietHours.Default.HeldUntil(At(2026, 9, 30, 9, 0, Edt)).ShouldBeNull();
    }

    [Fact]
    public void Quiet_hours_that_do_not_cross_midnight_hold_only_within_the_day()
    {
        var overTheWorkday = QuietHours.Of(9, 17, Eastern);

        overTheWorkday.HeldUntil(At(2026, 9, 29, 12, 0, Edt)).ShouldBe(At(2026, 9, 29, 17, 0, Edt));
        overTheWorkday.HeldUntil(At(2026, 9, 29, 17, 0, Edt)).ShouldBeNull();
        overTheWorkday.HeldUntil(At(2026, 9, 29, 8, 0, Edt)).ShouldBeNull();
    }

    [Fact]
    public void Quiet_hours_that_begin_at_midnight_are_held_until_they_end_that_morning()
    {
        var overnight = QuietHours.Of(0, 7, Eastern);

        overnight.HeldUntil(At(2026, 9, 30, 0, 0, Edt)).ShouldBe(At(2026, 9, 30, 7, 0, Edt));
        overnight.HeldUntil(At(2026, 9, 29, 23, 59, Edt)).ShouldBeNull();
    }

    [Fact]
    public void Quiet_hours_are_kept_in_the_members_own_zone()
    {
        // 03:00 UTC is 23:00 in New York, still 20:00 in Los Angeles.
        var instant = new DateTimeOffset(2026, 9, 30, 3, 0, 0, TimeSpan.Zero);

        QuietHours.Of(21, 9, Eastern).HeldUntil(instant).ShouldBe(At(2026, 9, 30, 9, 0, Edt));
        QuietHours.Of(21, 9, Pacific).HeldUntil(instant).ShouldBeNull();
    }

    [Fact]
    public void The_offset_an_instant_is_written_in_makes_no_difference()
    {
        var utc = new DateTimeOffset(2026, 9, 30, 3, 30, 0, TimeSpan.Zero);

        QuietHours.Default.HeldUntil(utc).ShouldBe(QuietHours.Default.HeldUntil(utc.ToOffset(Edt)));
        QuietHours.Default.HeldUntil(utc).ShouldBe(new DateTimeOffset(2026, 9, 30, 13, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Quiet_hours_across_the_spring_DST_change_end_at_nine_on_the_new_clock()
    {
        // US clocks jumped forward at 02:00 on 2026-03-08: the night is an hour short, so 09:00 is an hour earlier in UTC.
        var heldUntil = QuietHours.Default.HeldUntil(At(2026, 3, 7, 23, 0, Est));

        heldUntil.ShouldBe(At(2026, 3, 8, 9, 0, Edt));
        heldUntil.ShouldBe(new DateTimeOffset(2026, 3, 8, 13, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Quiet_hours_across_the_autumn_DST_change_end_at_nine_on_the_new_clock()
    {
        // US clocks fell back at 02:00 on 2026-11-01: the night is an hour long, so 09:00 is an hour later in UTC.
        var heldUntil = QuietHours.Default.HeldUntil(At(2026, 10, 31, 23, 0, Edt));

        heldUntil.ShouldBe(At(2026, 11, 1, 9, 0, Est));
        heldUntil.ShouldBe(new DateTimeOffset(2026, 11, 1, 14, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void An_instant_in_the_hour_the_clocks_repeat_is_held_by_the_clock_it_reads()
    {
        // 01:30 happens twice on 2026-11-01; both are in the night.
        QuietHours.Default.HeldUntil(At(2026, 11, 1, 1, 30, Edt)).ShouldBe(At(2026, 11, 1, 9, 0, Est));
        QuietHours.Default.HeldUntil(At(2026, 11, 1, 1, 30, Est)).ShouldBe(At(2026, 11, 1, 9, 0, Est));
    }

    [Fact]
    public void Quiet_hours_ending_in_the_hour_the_clocks_skip_end_when_the_clocks_jump()
    {
        // 02:00 does not exist on 2026-03-08; the first moment at or after it is 03:00 EDT.
        var untilTwo = QuietHours.Of(22, 2, Eastern);

        untilTwo.HeldUntil(At(2026, 3, 7, 23, 0, Est)).ShouldBe(At(2026, 3, 8, 3, 0, Edt));
    }

    [Fact]
    public void Quiet_hours_ending_in_the_hour_the_clocks_repeat_end_the_first_time_the_clock_reads_it()
    {
        // 01:00 happens twice on 2026-11-01; the night is over the first time.
        var untilOne = QuietHours.Of(21, 1, Eastern);

        untilOne.HeldUntil(At(2026, 10, 31, 23, 0, Edt)).ShouldBe(At(2026, 11, 1, 1, 0, Edt));
    }

    [Theory]
    [InlineData(-1, 9)]
    [InlineData(21, 24)]
    [InlineData(24, 9)]
    public void Quiet_hours_start_and_end_on_an_hour_of_the_day(int start, int end)
    {
        Should.Throw<DomainException>(() => QuietHours.Of(start, end, Eastern))
            .Message.ShouldBe("Quiet hours start and end on an hour, from 0 (midnight) to 23.");
    }

    [Fact]
    public void Quiet_hours_that_start_and_end_together_are_not_quiet_hours()
    {
        Should.Throw<DomainException>(() => QuietHours.Of(21, 21, Eastern))
            .Message.ShouldBe("Quiet hours must start and end at different hours.");
    }

    [Theory]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("Eastern Standard Time")]
    [InlineData("")]
    [InlineData(null)]
    public void Quiet_hours_are_kept_in_a_time_zone_that_exists(string? zone)
    {
        Should.Throw<DomainException>(() => QuietHours.Of(21, 9, zone))
            .Message.ShouldBe("That is not a time zone. Use one like America/New_York.");
    }

    [Theory]
    [InlineData("America/Chicago")]
    [InlineData("Pacific/Honolulu")]
    [InlineData("UTC")]
    public void Quiet_hours_can_be_kept_in_any_IANA_time_zone(string zone)
    {
        QuietHours.Of(21, 9, zone).TimeZone.ShouldBe(zone);
    }
}

using BallBank.Domain.Notifications;

namespace BallBank.Domain.Tests.Notifications;

public class ReminderStageTests
{
    private static readonly DateOnly DueDate = new(2026, 10, 1);

    private static ReminderStage? StageOn(DateOnly today, decimal balance = 50m) =>
        Reminders.StageOn(balance, DueDate, today);

    [Fact]
    public void Nothing_is_sent_before_the_three_days_out()
    {
        StageOn(DueDate.AddDays(-4)).ShouldBeNull();
        StageOn(DueDate.AddDays(-30)).ShouldBeNull();
    }

    [Fact]
    public void Three_days_before_the_due_date_is_the_first_stage()
    {
        StageOn(DueDate.AddDays(-3)).ShouldBe(ReminderStage.ThreeDaysBefore);
    }

    [Fact]
    public void The_days_between_three_days_out_and_the_due_date_stay_at_the_first_stage()
    {
        StageOn(DueDate.AddDays(-2)).ShouldBe(ReminderStage.ThreeDaysBefore);
        StageOn(DueDate.AddDays(-1)).ShouldBe(ReminderStage.ThreeDaysBefore);
    }

    [Fact]
    public void The_due_date_itself_is_the_on_the_day_stage()
    {
        StageOn(DueDate).ShouldBe(ReminderStage.OnTheDay);
    }

    [Fact]
    public void The_days_after_the_due_date_stay_at_the_on_the_day_stage_until_a_whole_week_has_passed()
    {
        StageOn(DueDate.AddDays(1)).ShouldBe(ReminderStage.OnTheDay);
        StageOn(DueDate.AddDays(6)).ShouldBe(ReminderStage.OnTheDay);
    }

    [Fact]
    public void Each_whole_week_overdue_is_a_stage_of_its_own()
    {
        StageOn(DueDate.AddDays(7)).ShouldBe(ReminderStage.WeeksOverdue(1));
        StageOn(DueDate.AddDays(14)).ShouldBe(ReminderStage.WeeksOverdue(2));
        StageOn(DueDate.AddDays(70)).ShouldBe(ReminderStage.WeeksOverdue(10));
    }

    [Fact]
    public void Between_whole_weeks_the_stage_is_the_last_week_reached()
    {
        StageOn(DueDate.AddDays(8)).ShouldBe(ReminderStage.WeeksOverdue(1));
        StageOn(DueDate.AddDays(13)).ShouldBe(ReminderStage.WeeksOverdue(1));
        StageOn(DueDate.AddDays(20)).ShouldBe(ReminderStage.WeeksOverdue(2));
    }

    [Fact]
    public void The_stages_are_distinct()
    {
        ReminderStage[] stages =
        [
            ReminderStage.ThreeDaysBefore, ReminderStage.OnTheDay, ReminderStage.WeeksOverdue(1), ReminderStage.WeeksOverdue(2),
        ];

        stages.Distinct().Count().ShouldBe(4);
    }

    [Theory]
    [InlineData(-3)]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(40)]
    public void A_member_who_owes_nothing_is_sent_nothing_at_any_stage(int daysFromDue)
    {
        StageOn(DueDate.AddDays(daysFromDue), balance: 0m).ShouldBeNull();
    }

    [Fact]
    public void A_member_the_pot_owes_is_sent_nothing()
    {
        StageOn(DueDate.AddDays(14), balance: -20m).ShouldBeNull();
    }

    [Fact]
    public void An_account_with_nothing_assessed_has_no_due_date_to_remind_about()
    {
        Reminders.StageOn(balance: 25m, earliestDueDate: null, today: DueDate).ShouldBeNull();
    }

    [Fact]
    public void A_stage_reads_as_what_it_is()
    {
        ReminderStage.ThreeDaysBefore.ToString().ShouldBe("3-days-before");
        ReminderStage.OnTheDay.ToString().ShouldBe("on-the-day");
        ReminderStage.WeeksOverdue(1).ToString().ShouldBe("1-week-overdue");
        ReminderStage.WeeksOverdue(3).ToString().ShouldBe("3-weeks-overdue");
    }

    [Fact]
    public void A_reminder_is_keyed_on_the_account_the_due_date_and_the_stage()
    {
        var account = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var member = Guid.Parse("33333333-3333-3333-3333-333333333333");

        Reminders.Key(Channels.Sms, member, account, DueDate, ReminderStage.OnTheDay)
            .ShouldBe("Sms/33333333-3333-3333-3333-333333333333/Reminder/22222222-2222-2222-2222-222222222222/2026-10-01/on-the-day");
    }

    [Fact]
    public void Another_stage_another_due_date_or_another_account_is_another_key()
    {
        var account = Guid.NewGuid();
        var member = Guid.NewGuid();
        var key = Reminders.Key(Channels.Sms, member, account, DueDate, ReminderStage.OnTheDay);

        Reminders.Key(Channels.Sms, member, account, DueDate, ReminderStage.WeeksOverdue(1)).ShouldNotBe(key);
        Reminders.Key(Channels.Sms, member, account, DueDate.AddDays(7), ReminderStage.OnTheDay).ShouldNotBe(key);
        Reminders.Key(Channels.Sms, member, Guid.NewGuid(), DueDate, ReminderStage.OnTheDay).ShouldNotBe(key);
        Reminders.Key(Channels.Sms, member, account, DueDate, ReminderStage.OnTheDay).ShouldBe(key);
    }
}

using BallBank.Domain.Treasury;

namespace BallBank.Domain.Tests.Treasury;

public class DelinquencyTests
{
    private static readonly DateOnly DueDate = new(2026, 9, 1);

    [Fact]
    public void A_member_who_owes_after_the_due_date_is_delinquent_by_the_days_since()
    {
        Delinquency.DaysOverdue(balance: 50m, earliestDueDate: DueDate, today: new DateOnly(2026, 9, 24)).ShouldBe(23);
    }

    [Fact]
    public void The_day_after_the_due_date_is_one_day_overdue()
    {
        Delinquency.DaysOverdue(50m, DueDate, DueDate.AddDays(1)).ShouldBe(1);
    }

    [Fact]
    public void On_the_due_date_nobody_is_delinquent_yet()
    {
        Delinquency.DaysOverdue(50m, DueDate, DueDate).ShouldBeNull();
    }

    [Fact]
    public void Before_the_due_date_nobody_is_delinquent()
    {
        Delinquency.DaysOverdue(50m, DueDate, DueDate.AddDays(-5)).ShouldBeNull();
    }

    [Fact]
    public void A_member_who_owes_nothing_is_not_delinquent_however_long_ago_it_was_due()
    {
        Delinquency.DaysOverdue(0m, DueDate, new DateOnly(2026, 12, 1)).ShouldBeNull();
    }

    [Fact]
    public void A_member_the_pot_owes_is_not_delinquent()
    {
        Delinquency.DaysOverdue(-20m, DueDate, new DateOnly(2026, 12, 1)).ShouldBeNull();
    }

    [Fact]
    public void An_account_with_nothing_assessed_has_no_due_date_to_pass()
    {
        Delinquency.DaysOverdue(balance: 25m, earliestDueDate: null, today: new DateOnly(2026, 12, 1)).ShouldBeNull();
    }
}

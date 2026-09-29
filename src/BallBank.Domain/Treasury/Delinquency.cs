namespace BallBank.Domain.Treasury;

/// <summary>
/// When an account is a delinquent: the member still owes, and the earliest date anything was due has
/// passed. Pure, with <c>today</c> a parameter like every clock in the domain.
/// </summary>
public static class Delinquency
{
    /// <summary>
    /// The whole days since the earliest due date, or <c>null</c> when the account is not delinquent:
    /// it owes nothing, has nothing assessed, or the earliest due date has not passed (the due date itself has not).
    /// </summary>
    public static int? DaysOverdue(decimal balance, DateOnly? earliestDueDate, DateOnly today)
    {
        if (balance <= 0 || earliestDueDate is not { } due)
        {
            return null;
        }

        var days = today.DayNumber - due.DayNumber;
        return days > 0 ? days : null;
    }
}

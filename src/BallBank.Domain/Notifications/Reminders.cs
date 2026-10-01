using System.Globalization;

namespace BallBank.Domain.Notifications;

/// <summary>
/// How far along the way to, and past, an earliest due date a member who still owes is reminded: three days
/// before, on the day, then every whole week overdue. Identified by its days from the due date, so a stage is
/// the same stage whenever it is worked out.
/// </summary>
public readonly record struct ReminderStage(int DaysAfterDue)
{
    public static readonly ReminderStage ThreeDaysBefore = new(-3);
    public static readonly ReminderStage OnTheDay = new(0);

    /// <param name="weeks">Whole weeks past the due date, at least one.</param>
    public static ReminderStage WeeksOverdue(int weeks) => new(weeks * 7);

    public override string ToString() => DaysAfterDue switch
    {
        < 0 => $"{-DaysAfterDue}-days-before",
        0 => "on-the-day",
        7 => "1-week-overdue",
        _ => $"{DaysAfterDue / 7}-weeks-overdue",
    };
}

/// <summary>
/// When a member who owes is reminded (ADR-0007). Pure, with <c>today</c> a parameter like every clock in the domain.
/// </summary>
public static class Reminders
{
    /// <summary>Why a reminder held or refused earlier was not sent: by the time it could go, the member owed nothing.</summary>
    public const string PaidUp = "The member has paid up";

    /// <summary>
    /// The latest stage reached as of <paramref name="today"/>, or <c>null</c> when nothing is to be said: the
    /// account owes nothing, has nothing assessed, or the due date is more than three days off. The latest stage
    /// rather than the stage the day falls on, so a reminder the tick missed is sent by the next one (each stage is
    /// sent once, so one that was sent is not sent again); only the latest is sent, so catching up is never a flood.
    /// </summary>
    public static ReminderStage? StageOn(decimal balance, DateOnly? earliestDueDate, DateOnly today)
    {
        if (balance <= 0 || earliestDueDate is not { } due)
        {
            return null;
        }

        var days = today.DayNumber - due.DayNumber;
        return days switch
        {
            < -3 => null,
            < 0 => ReminderStage.ThreeDaysBefore,
            < 7 => ReminderStage.OnTheDay,
            _ => ReminderStage.WeeksOverdue(days / 7),
        };
    }

    /// <summary>
    /// The dedupe key of a reminder: the same member is reminded about the same account's due date at the same
    /// stage once, however often the tick runs (<see cref="NotificationKey"/>).
    /// </summary>
    public static string Key(string channel, Guid memberId, Guid accountId, DateOnly dueDate, ReminderStage stage) =>
        NotificationKey.For(
            NotificationKinds.Reminder,
            $"{accountId}/{dueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}/{stage}",
            channel,
            memberId);
}

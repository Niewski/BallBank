using System.Globalization;

namespace BallBank.Domain.Notifications;

/// <summary>An ISO 8601 week, Monday to Sunday, read as <c>2026-W40</c>, which is how a digest's dedupe key names it.</summary>
public readonly record struct IsoWeek(int Year, int Number)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Year:0000}-W{Number:00}");
}

/// <summary>What one member owes the pot, as a digest tells of it; positive means the member owes, as a balance does everywhere.</summary>
public sealed record MemberBalance(string TeamName, decimal Balance);

/// <summary>When a league's weekly digest is due, and what identifies it (ADR-0007); weeks are kept on Eastern time, as quiet hours are.</summary>
public static class Digests
{
    /// <summary>The Monday hour, Eastern, from which a week's digest is due.</summary>
    private const int SlotHour = 9;

    /// <summary>Why a digest the channel refused was not sent after all: its week was over, or the league stopped wanting it.</summary>
    public const string NoLongerDue = "The digest is no longer due";

    /// <summary>
    /// The week due as of <paramref name="now"/>: from Monday 09:00 Eastern through Sunday, so a late tick still finds it;
    /// <c>null</c> before the slot. A week no tick found is not made up for by the next.
    /// </summary>
    public static IsoWeek? WeekDueAt(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(QuietHours.Default.TimeZone)).DateTime;
        if (local is { DayOfWeek: DayOfWeek.Monday, Hour: < SlotHour })
        {
            return null;
        }

        return new IsoWeek(ISOWeek.GetYear(local), ISOWeek.GetWeekOfYear(local));
    }

    /// <summary>The dedupe key of a league's digest for a week; its recipient is the league, whose Discord channel it goes to.</summary>
    public static string Key(Guid leagueId, IsoWeek week) =>
        NotificationKey.For(NotificationKinds.Digest, week.ToString(), Channels.Discord, leagueId);
}

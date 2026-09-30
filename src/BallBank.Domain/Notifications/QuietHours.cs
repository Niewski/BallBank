namespace BallBank.Domain.Notifications;

/// <summary>
/// The hours a member does not want to be texted, in their own time zone: from <paramref name="StartHour"/>
/// up to, but not including, <paramref name="EndHour"/>. They may cross midnight (21 to 9). A message
/// that falls inside them is held until they end, never dropped.
/// </summary>
/// <param name="StartHour">0 to 23, on the member's wall clock.</param>
/// <param name="EndHour">0 to 23, on the member's wall clock.</param>
/// <param name="TimeZone">An IANA id, e.g. <c>America/New_York</c>.</param>
public sealed record QuietHours(int StartHour, int EndHour, string TimeZone)
{
    /// <summary>What a member has until they choose: 9pm to 9am Eastern.</summary>
    public static readonly QuietHours Default = new(21, 9, "America/New_York");

    /// <summary>Quiet hours as a member entered them, refused if they are not hours of a day or the zone is unknown.</summary>
    public static QuietHours Of(int startHour, int endHour, string? timeZone)
    {
        if (startHour is < 0 or > 23 || endHour is < 0 or > 23)
        {
            throw new DomainException("Quiet hours start and end on an hour, from 0 (midnight) to 23.");
        }

        if (startHour == endHour)
        {
            throw new DomainException("Quiet hours must start and end at different hours.");
        }

        var id = timeZone?.Trim();

        // Only IANA ids, so a zone kept on one platform is found on every other; Windows ids have no slash.
        if (string.IsNullOrEmpty(id) || (id != "UTC" && !id.Contains('/')) || FindZone(id) is null)
        {
            throw new DomainException("That is not a time zone. Use one like America/New_York.");
        }

        return new QuietHours(startHour, endHour, id);
    }

    /// <summary>
    /// When quiet hours end, if <paramref name="instant"/> falls inside them; <c>null</c> when it does not.
    /// Read on the member's wall clock, so it holds across midnight and across a DST change: the end
    /// is the next time the clock reads the end hour, which after a night the clocks skipped through is
    /// the first moment at or after it, and in an hour the clocks repeat is the first time round.
    /// </summary>
    public DateTimeOffset? HeldUntil(DateTimeOffset instant)
    {
        var zone = FindZone(TimeZone) ?? throw new InvalidOperationException($"The time zone {TimeZone} is not known here.");
        var local = TimeZoneInfo.ConvertTime(instant, zone);
        var time = local.TimeOfDay;

        var start = TimeSpan.FromHours(StartHour);
        var end = TimeSpan.FromHours(EndHour);
        var crossesMidnight = StartHour > EndHour;

        var quiet = crossesMidnight ? time >= start || time < end : time >= start && time < end;
        if (!quiet)
        {
            return null;
        }

        var endsOn = crossesMidnight && time >= start ? local.Date.AddDays(1) : local.Date;
        return InstantOf(endsOn.AddHours(EndHour), zone);
    }

    private static DateTimeOffset InstantOf(DateTime wallClock, TimeZoneInfo zone)
    {
        // A wall-clock time the clocks skipped is the moment they jumped, which is the first time that exists.
        while (zone.IsInvalidTime(wallClock))
        {
            wallClock = wallClock.AddMinutes(1);
        }

        // One the clocks repeat is the first time round: the larger offset is the earlier instant.
        var offset = zone.IsAmbiguousTime(wallClock) ? zone.GetAmbiguousTimeOffsets(wallClock).Max() : zone.GetUtcOffset(wallClock);
        return new DateTimeOffset(wallClock, offset);
    }

    private static TimeZoneInfo? FindZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }
}

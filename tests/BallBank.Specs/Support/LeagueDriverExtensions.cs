namespace BallBank.Specs.Support;

public static class LeagueDriverExtensions
{
    /// <summary>Puts quiet hours around the league's "now", or half a day from it, so a scenario never depends on when it runs.</summary>
    public static Task KeepQuietHoursAroundNow(this ILeagueDriver league, string member, bool inside)
    {
        var hour = league.Now.UtcDateTime.Hour;
        return inside
            ? league.SetQuietHours(member, (hour + 23) % 24, (hour + 2) % 24, "UTC")
            : league.SetQuietHours(member, (hour + 12) % 24, (hour + 13) % 24, "UTC");
    }
}

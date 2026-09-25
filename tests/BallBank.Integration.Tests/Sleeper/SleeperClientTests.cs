using System.Diagnostics;
using BallBank.Api.Integrations.Sleeper;
using Microsoft.Extensions.DependencyInjection;

namespace BallBank.Integration.Tests.Sleeper;

/// <summary>The typed Sleeper client as the API registers it, over the fake Sleeper. No database.</summary>
public class SleeperClientTests
{
    private readonly FakeSleeper _sleeper = new();

    [Fact]
    public async Task The_current_season_is_the_one_Sleeper_says_leagues_are_in()
    {
        _sleeper.ServeJson("/state/nfl", """{ "season": "2026", "league_season": "2027", "season_type": "off" }""");

        var season = await Client().CurrentSeasonAsync(CancellationToken.None);

        season.ShouldBe("2027");
    }

    [Fact]
    public async Task A_league_reads_with_its_name_season_and_team_count()
    {
        var league = await Client().GetLeagueAsync(FakeSleeper.HollandHogsLeagueId, CancellationToken.None);

        league.ShouldBe(new SleeperLeague(FakeSleeper.HollandHogsLeagueId, "Holland Hogs", "2026", TotalRosters: 4));
    }

    [Fact]
    public async Task A_league_Sleeper_does_not_know_reads_as_none()
    {
        _sleeper.ServeJson("/league/999", "null");

        var league = await Client().GetLeagueAsync("999", CancellationToken.None);

        league.ShouldBeNull();
    }

    [Fact]
    public async Task A_leagues_users_read_with_display_names_team_names_and_the_commissioner()
    {
        var users = await Client().GetLeagueUsersAsync(FakeSleeper.HollandHogsLeagueId, CancellationToken.None);

        users.Select(u => (u.UserId, u.DisplayName, u.IsCommissioner, u.TeamName)).ShouldBe(
        [
            ("100000000000000001", "Jacob", true, "Hog Wild"),
            ("100000000000000002", "Sam", false, "Sam's Slammers"),
            ("100000000000000003", "Priya", false, null),
        ]);
    }

    [Fact]
    public async Task A_leagues_rosters_read_with_their_owners_including_the_orphan()
    {
        var rosters = await Client().GetLeagueRostersAsync(FakeSleeper.HollandHogsLeagueId, CancellationToken.None);

        rosters.ShouldBe(
        [
            new SleeperRoster(1, "100000000000000001"),
            new SleeperRoster(2, "100000000000000002"),
            new SleeperRoster(3, "100000000000000003"),
            new SleeperRoster(4, OwnerId: null),
        ]);
    }

    [Fact]
    public async Task Every_call_names_BallBank_as_the_caller()
    {
        await Client().CurrentSeasonAsync(CancellationToken.None);

        var request = _sleeper.Requests.ShouldHaveSingleItem();
        request.Headers.UserAgent.ToString().ShouldStartWith("BallBank/");
    }

    [Fact]
    public async Task Calls_about_a_league_are_traced_with_the_Sleeper_league_id()
    {
        var traced = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "BallBank.Api",
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (traced)
                {
                    traced.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        await Client().GetLeagueRostersAsync(FakeSleeper.HollandHogsLeagueId, CancellationToken.None);

        lock (traced)
        {
            traced.ShouldContain(a => (string?)a.GetTagItem("sleeper.league.id") == FakeSleeper.HollandHogsLeagueId);
        }
    }

    private SleeperClient Client()
    {
        var services = new ServiceCollection().AddSleeper();
        services.AddHttpClient<SleeperClient>().ConfigurePrimaryHttpMessageHandler(_sleeper.Handler);
        return services.BuildServiceProvider().GetRequiredService<SleeperClient>();
    }
}

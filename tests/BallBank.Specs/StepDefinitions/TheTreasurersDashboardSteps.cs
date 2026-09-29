using System.Globalization;
using BallBank.Specs.Support;
using Reqnroll;

namespace BallBank.Specs.StepDefinitions;

[Binding]
public sealed class TheTreasurersDashboardSteps(LeagueWorld world)
{
    private ILeagueDriver League => world.Driver;

    // Dues are due relative to the league's own today, as the API's clock is the wall clock over HTTP.
    [Given(@"^season dues of \$(\d+(?:\.\d{1,2})?) due (\d+) days (ago|from now)$")]
    public Task GivenSeasonDuesDueRelativeToToday(decimal amount, int days, string when) =>
        League.OpenSeason("2026", amount, League.Today.AddDays(when == "ago" ? -days : days));

    [When(@"^(\w+) reads the dashboard$")]
    public Task WhenAMemberReadsTheDashboard(string member) =>
        world.Attempt(() => League.ReadDashboard(readBy: member));

    [Then(@"^the dashboard shows$")]
    public async Task ThenTheDashboardShows(DataTable table)
    {
        var expected = table.Rows.Single();
        var dashboard = await Dashboard();

        dashboard.Assessed.ShouldBe(Money(expected["Assessed"]));
        dashboard.Confirmed.ShouldBe(Money(expected["Confirmed"]));
        dashboard.Refunded.ShouldBe(Money(expected["Refunded"]));
        dashboard.Pot.ShouldBe(Money(expected["Pot"]));
        dashboard.Outstanding.ShouldBe(Money(expected["Outstanding"]));
        dashboard.Owed.ShouldBe(Money(expected["Owed"]));
    }

    [Then(@"^the delinquents are$")]
    public async Task ThenTheDelinquentsAre(DataTable table)
    {
        var expected = table.Rows
            .Select(row => new DelinquentReading(row["Member"], Money(row["Balance"]), int.Parse(row["Days overdue"], CultureInfo.InvariantCulture)))
            .ToList();

        (await Dashboard()).Delinquents.ShouldBe(expected);
    }

    [Then(@"^the dashboard shows no delinquents$")]
    public async Task ThenTheDashboardShowsNoDelinquents() => (await Dashboard()).Delinquents.ShouldBeEmpty();

    private Task<DashboardReading> Dashboard() => League.ReadDashboard();

    private static decimal Money(string amount) => decimal.Parse(amount.TrimStart('$'), CultureInfo.InvariantCulture);
}

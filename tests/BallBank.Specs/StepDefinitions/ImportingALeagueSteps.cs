using BallBank.Specs.Support;
using Reqnroll;

namespace BallBank.Specs.StepDefinitions;

[Binding]
public sealed class ImportingALeagueSteps(ImportWorld world)
{
    [Given(@"^the Sleeper league ""([^""]*)"" for the (\d{4}) season with these teams:$")]
    public void GivenTheSleeperLeague(string name, string season, DataTable teams) =>
        world.SleeperLeague(name, season, teams.Rows.Select(row => (
            int.Parse(row["roster"]),
            Blank(row["owner"]),
            Blank(row["team name"]),
            Yes(row["commissioner"]))));

    [Given(@"^(\w+) has imported the league$")]
    public void GivenImported(string person)
    {
        world.Import(person);
        world.Refusal.ShouldBeNull();
    }

    [When(@"^(\w+) imports the (?:league|same Sleeper league as another league)$")]
    public void WhenImports(string person) => world.Import(person);

    [Then(@"^(\w+) holds the member ""([^""]*)""$")]
    public void ThenHolds(string person, string teamName)
    {
        var member = world.RequireLeague().MemberHeldBy(ImportWorld.Subject(person));
        member.ShouldNotBeNull();
        member.TeamName.ShouldBe(teamName);
    }

    [Then(@"^(\w+) is a treasurer$")]
    public void ThenIsATreasurer(string person)
    {
        var member = world.RequireLeague().MemberHeldBy(ImportWorld.Subject(person));
        member.ShouldNotBeNull();
        member.IsTreasurer.ShouldBeTrue();
    }

    [Then(@"^the league has these members:$")]
    public void ThenTheLeagueHasMembers(DataTable expected) =>
        world.RequireLeague().Members
            .Select(m => (m.TeamName, world.NameOf(m.SleeperUserId), m.IsClaimed, m.SuggestedTreasurer))
            .ShouldBe(
                expected.Rows.Select(row => (row["team name"], row["owner on Sleeper"], Yes(row["claimed"]), Yes(row["suggested treasurer"]))),
                ignoreOrder: true);

    [Then(@"^the import is refused$")]
    public void ThenRefused() => world.Refusal.ShouldNotBeNull();

    private static string? Blank(string cell) => string.IsNullOrWhiteSpace(cell) ? null : cell;

    private static bool Yes(string cell) => cell.Equals("yes", StringComparison.OrdinalIgnoreCase);
}

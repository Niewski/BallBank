using System.Globalization;
using BallBank.Domain.Treasury;
using BallBank.Specs.Support;
using Reqnroll;

namespace BallBank.Specs.StepDefinitions;

[Binding]
public sealed class OpeningASeasonSteps(LeagueWorld world)
{
    private ILeagueDriver League => world.Driver;

    [Given(@"^a league with members (.+)$")]
    public Task GivenALeagueWithMembers(string members) =>
        League.OpenLeague(members.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

    [Given(@"^the treasurer has opened the ""([^""]*)"" season with dues of \$(\d+(?:\.\d{1,2})?) due on (\d{4}-\d{2}-\d{2})$")]
    public Task GivenTheTreasurerHasOpenedTheSeason(string label, decimal duesAmount, string dueDate) =>
        League.OpenSeason(label, duesAmount, DateOnly.Parse(dueDate, CultureInfo.InvariantCulture));

    [When(@"^the treasurer opens the ""([^""]*)"" season with dues of \$(\d+(?:\.\d{1,2})?) due on (\d{4}-\d{2}-\d{2})(?: again)?$")]
    public Task WhenTheTreasurerOpensTheSeason(string label, decimal duesAmount, string dueDate) =>
        League.OpenSeason(label, duesAmount, DateOnly.Parse(dueDate, CultureInfo.InvariantCulture));

    [Given(@"^importing the league again adds (\w+)$")]
    public Task GivenImportingTheLeagueAgainAdds(string member) => League.AddMember(member);

    [When(@"^the treasurer assesses (\w+) \$(\d+(?:\.\d{1,2})?) for ""([^""]*)"" due on (\d{4}-\d{2}-\d{2})$")]
    public Task WhenTheTreasurerAssesses(string member, decimal amount, string memo, string dueDate) =>
        League.Assess(member, amount, memo, DateOnly.Parse(dueDate, CultureInfo.InvariantCulture));

    [Then(@"^(\w+) owes \$(\d+(?:\.\d{1,2})?)$")]
    public async Task ThenOwes(string member, decimal expected) => (await League.Balance(member)).ShouldBe(expected);

    [Then(@"^the season was opened once$")]
    public async Task ThenTheSeasonWasOpenedOnce() => (await League.History()).OfType<SeasonOpened>().ShouldHaveSingleItem();
}

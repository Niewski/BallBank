using System.Globalization;
using BallBank.Domain.Treasury;
using BallBank.Specs.Support;
using Reqnroll;

namespace BallBank.Specs.StepDefinitions;

[Binding]
public sealed class OpeningASeasonSteps(SeasonWorld world)
{
    [Given(@"^a league with members (.+)$")]
    public void GivenALeagueWithMembers(string members) =>
        world.AddMembers(members.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

    [Given(@"^the treasurer has opened the ""([^""]*)"" season with dues of \$(\d+(?:\.\d{1,2})?) due on (\d{4}-\d{2}-\d{2})$")]
    public void GivenTheTreasurerHasOpenedTheSeason(string label, decimal duesAmount, string dueDate) =>
        world.OpenSeason(label, duesAmount, DateOnly.Parse(dueDate, CultureInfo.InvariantCulture));

    [When(@"^the treasurer opens the ""([^""]*)"" season with dues of \$(\d+(?:\.\d{1,2})?) due on (\d{4}-\d{2}-\d{2})(?: again)?$")]
    public void WhenTheTreasurerOpensTheSeason(string label, decimal duesAmount, string dueDate) =>
        world.OpenSeason(label, duesAmount, DateOnly.Parse(dueDate, CultureInfo.InvariantCulture));

    [Then(@"^(\w+) owes \$(\d+(?:\.\d{1,2})?)$")]
    public void ThenOwes(string member, decimal expected) => world.Account(member).Balance.ShouldBe(expected);

    [Then(@"^the season was opened once$")]
    public void ThenTheSeasonWasOpenedOnce() => world.History.OfType<SeasonOpened>().ShouldHaveSingleItem();
}

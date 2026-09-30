using BallBank.Specs.Support;
using Reqnroll;

namespace BallBank.Specs.StepDefinitions;

[Binding]
public sealed class AnnouncingToDiscordSteps(LeagueWorld world)
{
    private ILeagueDriver League => world.Driver;

    [Given(@"^the treasurer has connected Discord$")]
    [When(@"^the treasurer connects Discord$")]
    public Task GivenTheTreasurerHasConnectedDiscord() => League.ConnectDiscord(announcePayments: false);

    [Given(@"^the treasurer has connected Discord, announcing confirmed payments$")]
    public Task GivenTheTreasurerHasConnectedDiscordAnnouncingPayments() => League.ConnectDiscord(announcePayments: true);

    [Then(@"^Discord was told:$")]
    public async Task ThenDiscordWasTold(DataTable messages) =>
        (await League.PostedToDiscord()).ShouldBe(messages.Rows.Select(row => row[0]).ToList());
}

using BallBank.Specs.Support;
using Reqnroll;

namespace BallBank.Specs.StepDefinitions;

[Binding]
public sealed class RejectingAPaymentSteps(LeagueWorld world)
{
    [When(@"^the treasurer rejects (\w+)'s payment without a reason$")]
    public Task WhenTheTreasurerRejectsWithoutAReason(string member) =>
        world.Attempt(() => world.Driver.RejectLatest(member, reason: ""));
}

using BallBank.Specs.Support;
using Reqnroll;

namespace BallBank.Specs.StepDefinitions;

[Binding]
public sealed class RejectingAPaymentSteps(LeagueWorld world)
{
    [When(@"^the treasurer rejects (\w+)'s payment without a reason$")]
    public void WhenTheTreasurerRejectsWithoutAReason(string member) =>
        world.Attempt(() => world.RejectLatest(member, reason: ""));
}

using BallBank.Specs.Support;
using Reqnroll;

namespace BallBank.Specs.StepDefinitions;

[Binding]
public sealed class PostingAnAdjustmentSteps(LeagueWorld world)
{
    private ILeagueDriver League => world.Driver;

    [When(@"^the treasurer adjusts (\w+)'s balance by (-?)\$(\d+(?:\.\d+)?) because ""([^""]*)""$")]
    public Task WhenTheTreasurerAdjusts(string member, string sign, decimal amount, string reason) =>
        world.Attempt(() => League.PostAdjustment(member, Signed(sign, amount), reason));

    [When(@"^the treasurer refunds (\w+) \$(\d+(?:\.\d+)?) because ""([^""]*)""$")]
    public Task WhenTheTreasurerRefunds(string member, decimal amount, string reason) =>
        world.Attempt(() => League.PostAdjustment(member, amount, reason, refund: true));

    [When(@"^the treasurer adjusts (\w+)'s balance by (-?)\$(\d+(?:\.\d+)?) without a reason$")]
    public Task WhenTheTreasurerAdjustsWithoutAReason(string member, string sign, decimal amount) =>
        world.Attempt(() => League.PostAdjustment(member, Signed(sign, amount), reason: ""));

    [When(@"^(\w+) adjusts (\w+)'s balance by (-?)\$(\d+(?:\.\d+)?) because ""([^""]*)""$")]
    public Task WhenAMemberAdjusts(string poster, string member, string sign, decimal amount, string reason) =>
        world.Attempt(() => League.PostAdjustment(member, Signed(sign, amount), reason, postedBy: poster));

    [Then(@"^(\w+) is not allowed to$")]
    public void ThenNotAllowed(string member) => world.NotAllowed.ShouldBe(member);

    private static decimal Signed(string sign, decimal amount) => sign == "-" ? -amount : amount;
}

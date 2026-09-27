using BallBank.Specs.Support;
using Reqnroll;

namespace BallBank.Specs.StepDefinitions;

[Binding]
public sealed class PostingAnAdjustmentSteps(LeagueWorld world)
{
    [When(@"^the treasurer adjusts (\w+)'s balance by (-?)\$(\d+(?:\.\d+)?) because ""([^""]*)""$")]
    public void WhenTheTreasurerAdjusts(string member, string sign, decimal amount, string reason) =>
        world.Attempt(() => world.PostAdjustment(member, Signed(sign, amount), reason));

    [When(@"^the treasurer refunds (\w+) \$(\d+(?:\.\d+)?) because ""([^""]*)""$")]
    public void WhenTheTreasurerRefunds(string member, decimal amount, string reason) =>
        world.Attempt(() => world.PostAdjustment(member, amount, reason, refund: true));

    [When(@"^the treasurer adjusts (\w+)'s balance by (-?)\$(\d+(?:\.\d+)?) without a reason$")]
    public void WhenTheTreasurerAdjustsWithoutAReason(string member, string sign, decimal amount) =>
        world.Attempt(() => world.PostAdjustment(member, Signed(sign, amount), reason: ""));

    [When(@"^(\w+) adjusts (\w+)'s balance by (-?)\$(\d+(?:\.\d+)?) because ""([^""]*)""$")]
    public void WhenAMemberAdjusts(string poster, string member, string sign, decimal amount, string reason) =>
        world.Attempt(() => world.PostAdjustment(member, Signed(sign, amount), reason, postedBy: poster));

    [Then(@"^(\w+) is not allowed to$")]
    public void ThenNotAllowed(string member) => world.RefusedPoster.ShouldBe(member);

    private static decimal Signed(string sign, decimal amount) => sign == "-" ? -amount : amount;
}

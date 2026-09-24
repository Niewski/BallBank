using System.Globalization;
using BallBank.Domain.Treasury;
using BallBank.Specs.Support;
using Reqnroll;

namespace BallBank.Specs.StepDefinitions;

[Binding]
public sealed class PayingDuesSteps(LeagueWorld world)
{
    [Given(@"^a league ""([^""]*)"" with members (.+)$")]
    public void GivenALeagueWithMembers(string leagueName, string members)
    {
        _ = leagueName; // naming is a Membership concern; the treasury only needs the accounts
        world.OpenLeague(members.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    }

    [Given(@"^season dues of \$(\d+(?:\.\d{1,2})?) due on (\d{4}-\d{2}-\d{2})$")]
    public void GivenSeasonDues(decimal amount, string dueDate) =>
        world.AssessEveryone(amount, DateOnly.Parse(dueDate, CultureInfo.InvariantCulture));

    [When(@"^(\w+) attests a \$(\d+(?:\.\d{1,2})?) (\w+) payment with reference ""([^""]*)""$")]
    public void WhenAMemberAttestsAPayment(string member, decimal amount, string rail, string reference) =>
        world.Attest(member, amount, Enum.Parse<PaymentRail>(rail, ignoreCase: true), reference);

    [When(@"^(\w+) attests that same payment again$")]
    public void WhenAMemberAttestsTheSamePaymentAgain(string member) => world.AttestSameAgain(member);

    [When(@"^the treasurer confirms (\w+)'s payment$")]
    public void WhenTheTreasurerConfirms(string member) => world.ConfirmLatest(member);

    [When(@"^the treasurer rejects (\w+)'s payment because ""([^""]*)""$")]
    public void WhenTheTreasurerRejects(string member, string reason) => world.RejectLatest(member, reason);

    [Then(@"^(\w+)'s balance is \$(\d+(?:\.\d{1,2})?)$")]
    public void ThenTheBalanceIs(string member, decimal expected) =>
        world.Account(member).Balance.ShouldBe(expected);

    [Then(@"^the league pot is \$(\d+(?:\.\d{1,2})?)$")]
    public void ThenTheLeaguePotIs(decimal expected) => world.Pot.ShouldBe(expected);

    [Then(@"^(\w+) has (\d+) pending payments?$")]
    public void ThenPendingPayments(string member, int expected) =>
        world.Account(member).PendingAttestations.Count().ShouldBe(expected);

    [Then(@"^the history shows the treasurer confirmed (\w+)'s payment$")]
    public void ThenTheHistoryShowsTheConfirmation(string member)
    {
        var attestationIds = world.Account(member).Attestations.Select(a => a.AttestationId).ToHashSet();

        world.History
            .OfType<PaymentConfirmed>()
            .ShouldContain(e => e.ConfirmedBy == world.TreasurerId && attestationIds.Contains(e.AttestationId));
    }
}

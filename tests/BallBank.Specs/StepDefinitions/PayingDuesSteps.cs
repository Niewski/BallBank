using System.Globalization;
using BallBank.Domain.Treasury;
using BallBank.Specs.Support;
using Reqnroll;

namespace BallBank.Specs.StepDefinitions;

[Binding]
public sealed class PayingDuesSteps(LeagueWorld world)
{
    private ILeagueDriver League => world.Driver;

    [Given(@"^a league ""([^""]*)"" with members (.+)$")]
    public Task GivenALeagueWithMembers(string leagueName, string members)
    {
        _ = leagueName; // naming is a Membership concern; the treasury only needs the accounts
        return League.OpenLeague(members.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    }

    [Given(@"^season dues of \$(\d+(?:\.\d{1,2})?) due on (\d{4}-\d{2}-\d{2})$")]
    public Task GivenSeasonDues(decimal amount, string dueDate) =>
        League.OpenSeason("2026", amount, DateOnly.Parse(dueDate, CultureInfo.InvariantCulture));

    [When(@"^(\w+) attests a \$(\d+(?:\.\d{1,2})?) (\w+) payment with reference ""([^""]*)""$")]
    public Task WhenAMemberAttestsAPayment(string member, decimal amount, string rail, string reference) =>
        League.Attest(member, amount, Enum.Parse<PaymentRail>(rail, ignoreCase: true), reference);

    // Any number of decimals, so a fraction of a cent can be tried and refused.
    [When(@"^(\w+) attests a \$(\d+(?:\.\d+)?) Cash payment$")]
    public Task WhenAMemberAttestsACashPayment(string member, decimal amount) =>
        world.Attempt(() => League.Attest(member, amount, PaymentRail.Cash, reference: null));

    [When(@"^the treasurer attests a \$(\d+(?:\.\d{1,2})?) Cash payment for (\w+)$")]
    public Task WhenTheTreasurerAttestsACashPaymentFor(decimal amount, string member) =>
        League.Attest(member, amount, PaymentRail.Cash, reference: null, byTreasurer: true);

    [When(@"^(\w+) attests that same payment again$")]
    public Task WhenAMemberAttestsTheSamePaymentAgain(string member) => League.AttestSameAgain(member);

    [When(@"^the treasurer confirms (\w+)'s payment$")]
    public Task WhenTheTreasurerConfirms(string member) => League.ConfirmLatest(member);

    [When(@"^the treasurer rejects (\w+)'s payment because ""([^""]*)""$")]
    public Task WhenTheTreasurerRejects(string member, string reason) =>
        world.Attempt(() => League.RejectLatest(member, reason));

    [Then(@"^(\w+)'s balance is \$(\d+(?:\.\d{1,2})?)$")]
    public async Task ThenTheBalanceIs(string member, decimal expected) =>
        (await League.Balance(member)).ShouldBe(expected);

    [Then(@"^the league pot is \$(\d+(?:\.\d{1,2})?)$")]
    public async Task ThenTheLeaguePotIs(decimal expected) => (await League.Pot()).ShouldBe(expected);

    [Then(@"^(\w+) has (\d+) pending payments?$")]
    public async Task ThenPendingPayments(string member, int expected) =>
        (await League.PendingPayments(member)).ShouldBe(expected);

    [Then(@"^the pot owes (\w+) \$(\d+(?:\.\d{1,2})?)$")]
    public async Task ThenThePotOwes(string member, decimal expected) =>
        (await League.Balance(member)).ShouldBe(-expected);

    [Then(@"^the (?:payment|adjustment) is refused because ""([^""]*)""$")]
    public void ThenTheCommandIsRefused(string reason) => world.Refusal.ShouldNotBeNull().Message.ShouldBe(reason);

    [Then(@"^the history shows the treasurer attested (\w+)'s payment$")]
    public async Task ThenTheHistoryShowsTheTreasurerAttested(string member) =>
        (await League.AccountHistory(member)).OfType<PaymentAttested>().ShouldContain(e => e.AttestedBy == League.TreasurerId);

    [Then(@"^the history shows the treasurer confirmed (\w+)'s payment$")]
    public async Task ThenTheHistoryShowsTheConfirmation(string member) =>
        (await League.AccountHistory(member)).OfType<PaymentConfirmed>().ShouldContain(e => e.ConfirmedBy == League.TreasurerId);

    [Then(@"^(\w+)'s payment was confirmed once$")]
    public async Task ThenConfirmedOnce(string member) =>
        (await League.AccountHistory(member)).OfType<PaymentConfirmed>().ShouldHaveSingleItem();
}

using System.Net;
using BallBank.Api;
using BallBank.Specs.Support;
using Reqnroll;

namespace BallBank.Specs.StepDefinitions;

/// <summary>Steps about requests themselves, for scenarios tagged <c>@http</c>, which run on the HTTP driver only.</summary>
[Binding]
public sealed class OverHttpSteps(LeagueWorld world)
{
    private Answer? _first;
    private Answer? _retry;
    private Answer[] _atOnce = [];
    private string? _confirmedFor;

    private HttpLeagueDriver League =>
        world.Driver as HttpLeagueDriver ?? throw new InvalidOperationException($"This step runs over HTTP only: set {SpecsDriver.Variable}=http.");

    [Given(@"^(\w+) is a treasurer too$")]
    public Task GivenATreasurerToo(string member) => League.AppointTreasurer(member);

    [When(@"^(\w+)'s app sends that request again with the same idempotency key$")]
    public async Task WhenTheAppRetries(string member)
    {
        League.Last.ShouldNotBeNull().Request.Person.ShouldBe(member);
        (_first, _retry) = await League.RetryLast();
    }

    [When(@"^the treasurer and (\w+) confirm (\w+)'s payment at once$")]
    public async Task WhenConfirmedAtOnce(string otherTreasurer, string member)
    {
        _confirmedFor = member;
        _atOnce = await League.ConfirmAtOnce(member, [HttpLeagueDriver.Treasurer, otherTreasurer]);
    }

    [Then(@"^the retry is answered the same as the first request$")]
    public void ThenAnsweredTheSame()
    {
        _retry.ShouldNotBeNull().Status.ShouldBe(_first.ShouldNotBeNull().Status);
        _retry.Body.ShouldBe(_first.Body);
    }

    [Then(@"^the other treasurer is told the account changed and sees its current version$")]
    public async Task ThenTheOtherIsToldTheAccountChanged()
    {
        _atOnce.Count(a => a.Status == HttpStatusCode.OK).ShouldBe(1);
        var other = _atOnce.Single(a => a.Status != HttpStatusCode.OK);

        other.Status.ShouldBe(HttpStatusCode.Conflict);
        var problem = other.Problem().ShouldNotBeNull();
        problem.Type.ShouldBe(ExpectedVersion.ConflictType);
        problem.CurrentVersion.ShouldBe(await League.Version(_confirmedFor.ShouldNotBeNull()));
    }
}

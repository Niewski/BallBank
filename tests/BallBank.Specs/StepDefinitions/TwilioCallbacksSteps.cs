using System.Globalization;
using System.Net;
using BallBank.Specs.Support;
using Reqnroll;

namespace BallBank.Specs.StepDefinitions;

/// <summary>What Twilio calls back with, for scenarios tagged <c>@http</c>: a callback is a request, so there is none in memory.</summary>
[Binding]
public sealed class TwilioCallbacksSteps(LeagueWorld world)
{
    private HttpStatusCode? _answered;
    private HttpLeagueDriver? _second;

    private HttpLeagueDriver League =>
        world.Driver as HttpLeagueDriver ?? throw new InvalidOperationException($"This step runs over HTTP only: set {SpecsDriver.Variable}=http.");

    private HttpLeagueDriver SecondLeague => _second ?? throw new InvalidOperationException("Put the member in a second league first.");

    [Given(@"^(\w+) is also in a second league at that number, opted in and outside their quiet hours$")]
    public async Task GivenAlsoInASecondLeague(string member)
    {
        var phone = (await League.ReadTexting(member)).Consent.ShouldNotBeNull().Phone;

        _second = new HttpLeagueDriver();
        await _second.OpenLeague([HttpLeagueDriver.Treasurer, member]);
        await _second.OpenSeason("2026", 50m, new DateOnly(2026, 10, 1));
        await _second.RecordContactDetails(member, phone);
        await _second.OptInToTexts(member);
        await _second.KeepQuietHoursAroundNow(member, inside: false);
    }

    [When(@"^Twilio reports (\w+)'s text as ""([^""]*)""$")]
    public async Task WhenTwilioReports(string member, string status) =>
        _answered = await League.ReportOnLatestText(member, status);

    [When(@"^someone who is not Twilio reports (\w+)'s text as ""([^""]*)""$")]
    public async Task WhenSomeoneElseReports(string member, string status) =>
        _answered = await League.ReportOnLatestText(member, status, forged: true);

    [When(@"^(\w+) replies ""([^""]*)""$")]
    public async Task WhenRepliesToTheNumber(string member, string body) =>
        _answered = await League.Reply(member, body);

    [When(@"^someone who is not Twilio sends ""([^""]*)"" from (\w+)'s number$")]
    public async Task WhenSomeoneElseReplies(string body, string member) =>
        _answered = await League.Reply(member, body, forged: true);

    [When(@"^the second league's treasurer assesses (\w+) \$(\d+(?:\.\d{1,2})?) for ""([^""]*)"" due on (\d{4}-\d{2}-\d{2})$")]
    public Task WhenTheSecondLeaguesTreasurerAssesses(string member, decimal amount, string memo, string dueDate) =>
        SecondLeague.Assess(member, amount, memo, DateOnly.Parse(dueDate, CultureInfo.InvariantCulture));

    [Then(@"^Twilio is answered that all is well$")]
    public void ThenAnsweredOk() => _answered.ShouldBe(HttpStatusCode.OK);

    [Then(@"^the request is refused$")]
    public void ThenRefused() => _answered.ShouldBe(HttpStatusCode.Forbidden);

    [Then(@"^(\w+)'s text shows as (\w+)$")]
    public async Task ThenTextShowsAs(string member, string status) =>
        (await League.LatestTextStatus(member)).ToLowerInvariant().ShouldBe(status);

    [Then(@"^(\w+) is opted out of texts$")]
    public async Task ThenOptedOut(string member) => (await League.IsOptedOut(member)).ShouldBeTrue();

    [Then(@"^(\w+) is not opted out of texts$")]
    public async Task ThenNotOptedOut(string member) => (await League.IsOptedOut(member)).ShouldBeFalse();

    [Then(@"^(\w+) was not texted in the second league$")]
    public async Task ThenNotTextedInTheSecondLeague(string member) =>
        (await SecondLeague.TextsSentTo(member)).ShouldBeEmpty();

    [Then(@"^(\w+) was texted once in the second league$")]
    public async Task ThenTextedOnceInTheSecondLeague(string member) =>
        (await SecondLeague.TextsSentTo(member)).Count.ShouldBe(1);
}

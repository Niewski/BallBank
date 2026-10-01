using System.Text.RegularExpressions;
using BallBank.Domain.Notifications;
using BallBank.Specs.Support;
using Reqnroll;

namespace BallBank.Specs.StepDefinitions;

[Binding]
public sealed partial class TextingMembersSteps(LeagueWorld world)
{
    private ILeagueDriver League => world.Driver;

    [Given(@"^(\w+) has recorded the phone number ""([^""]*)""$")]
    public Task GivenRecordedPhone(string member, string phone) => League.RecordContactDetails(member, phone);

    [When(@"^(\w+) records the phone number ""([^""]*)""$")]
    public Task WhenRecordsPhone(string member, string phone) => League.RecordContactDetails(member, phone);

    [When(@"^(\w+) records the phone number ""([^""]*)"" for (\w+)$")]
    public Task WhenRecordsPhoneFor(string recordedBy, string phone, string member) =>
        League.RecordContactDetails(member, phone, recordedBy);

    [Given(@"^(\w+) has opted in to texts$")]
    public Task GivenOptedIn(string member) => League.OptInToTexts(member);

    [When(@"^(\w+) opts in to texts$")]
    public Task WhenOptsIn(string member) => world.Attempt(() => League.OptInToTexts(member));

    [When(@"^(\w+) opts (\w+) in to texts$")]
    public Task WhenOptsAnotherIn(string optedInBy, string member) => world.Attempt(() => League.OptInToTexts(member, optedInBy));

    [When(@"^(\w+) opts out of texts$")]
    public Task WhenOptsOut(string member) => League.OptOutOfTexts(member);

    [When(@"^(\w+) sets their quiet hours from (\d+) to (\d+) in ""([^""]*)""$")]
    public Task WhenSetsQuietHours(string member, int startHour, int endHour, string timeZone) =>
        League.SetQuietHours(member, startHour, endHour, timeZone);

    // The league and the clock are the driver's, so quiet hours are put an hour either side of its "now" and
    // never depend on when the scenario happens to run.
    [Given(@"^it is (inside|outside) (\w+)'s quiet hours$")]
    public Task GivenQuietHoursAroundNow(string where, string member)
    {
        var hour = League.Now.UtcDateTime.Hour;
        return where == "inside"
            ? League.SetQuietHours(member, (hour + 23) % 24, (hour + 2) % 24, "UTC")
            : League.SetQuietHours(member, (hour + 12) % 24, (hour + 13) % 24, "UTC");
    }

    [Then(@"^(\w+) was texted:$")]
    public async Task ThenTexted(string member, DataTable messages)
    {
        var expected = new List<string>();
        foreach (var row in messages.Rows)
        {
            expected.Add(await WithStatementLinks(row[0]));
        }

        (await League.TextsSentTo(member)).ShouldBe(expected);
    }

    [Then(@"^(\w+) was not texted$")]
    public async Task ThenNotTexted(string member) => (await League.TextsSentTo(member)).ShouldBeEmpty();

    // A statement is at a link naming the league and the account, which differ in every scenario: "{Sam's statement}".
    private async Task<string> WithStatementLinks(string message)
    {
        foreach (Match named in StatementLink().Matches(message))
        {
            message = message.Replace(named.Value, await League.StatementLink(named.Groups[1].Value));
        }

        return message;
    }

    [GeneratedRegex(@"\{(\w+)'s statement\}")]
    private static partial Regex StatementLink();

    [Then(@"^(\w+) is opted in to texts at ""([^""]*)""$")]
    public async Task ThenOptedInAt(string member, string phone)
    {
        var reading = await League.ReadTexting(member);

        reading.OptedIn.ShouldBeTrue();
        reading.Consent.ShouldNotBeNull().Phone.ShouldBe(phone);
    }

    [Then(@"^(\w+) is not opted in to texts$")]
    public async Task ThenNotOptedIn(string member) => (await League.ReadTexting(member)).OptedIn.ShouldBeFalse();

    [Then(@"^(\w+)'s quiet hours are (\d+) to (\d+) in ""([^""]*)""$")]
    public async Task ThenQuietHours(string member, int startHour, int endHour, string timeZone) =>
        (await League.ReadTexting(member)).QuietHours.ShouldBe(new QuietHours(startHour, endHour, timeZone));

    [Then(@"^opting in is refused because ""([^""]*)""$")]
    public void ThenRefused(string reason) => world.Refusal.ShouldNotBeNull().Message.ShouldBe(reason);
}

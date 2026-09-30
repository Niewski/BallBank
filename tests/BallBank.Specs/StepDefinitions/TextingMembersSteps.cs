using BallBank.Domain.Notifications;
using BallBank.Specs.Support;
using Reqnroll;

namespace BallBank.Specs.StepDefinitions;

[Binding]
public sealed class TextingMembersSteps(LeagueWorld world)
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

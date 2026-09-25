using BallBank.Domain;
using BallBank.Domain.Membership;
using BallBank.Specs.Support;
using Reqnroll;

namespace BallBank.Specs.StepDefinitions;

/// <summary>
/// Contact details over the league <see cref="ImportWorld"/> imported. What is kept for each team
/// stands in for the API's <c>MemberContact</c> documents: replaced on every change, gone once empty.
/// </summary>
[Binding]
public sealed class KeepingContactDetailsSteps(ImportWorld world)
{
    private readonly Dictionary<string, ContactDetails> _kept = new();
    private DomainException? _refusal;

    [When(@"^(\w+) records these contact details for ""([^""]*)"":$")]
    public void WhenRecords(string person, string teamName, DataTable details)
    {
        var row = details.Rows.Single();
        Record(person, teamName, Blank(row["email"]), Blank(row["phone"]), Blank(row["discord username"]));
    }

    [Given(@"^(\w+) has recorded the phone number ""([^""]*)"" for ""([^""]*)""$")]
    public void GivenRecordedPhone(string person, string phone, string teamName)
    {
        Record(person, teamName, email: null, phone, discordUsername: null);
        _refusal.ShouldBeNull();
    }

    [When(@"^(\w+) records the phone number ""([^""]*)"" for ""([^""]*)""$")]
    public void WhenRecordsPhone(string person, string phone, string teamName) =>
        Record(person, teamName, email: null, phone, discordUsername: null);

    [When(@"^(\w+) records the Discord username ""([^""]*)"" for ""([^""]*)""$")]
    public void WhenRecordsDiscordUsername(string person, string discordUsername, string teamName) =>
        Record(person, teamName, email: null, phone: null, discordUsername);

    [When(@"^(\w+) clears the contact details for ""([^""]*)""$")]
    public void WhenClears(string person, string teamName) =>
        Record(person, teamName, email: null, phone: null, discordUsername: null);

    [Then(@"^""([^""]*)"" has these contact details:$")]
    public void ThenHas(string teamName, DataTable expected)
    {
        var row = expected.Rows.Single();
        _kept.GetValueOrDefault(teamName)
            .ShouldBe(new ContactDetails(Blank(row["email"]), Blank(row["phone"]), Blank(row["discord username"])));
    }

    [Then(@"^""([^""]*)"" has the phone number ""([^""]*)""$")]
    public void ThenHasPhone(string teamName, string phone) =>
        _kept.GetValueOrDefault(teamName).ShouldNotBeNull().Phone.ShouldBe(phone);

    [Then(@"^""([^""]*)"" has the Discord username ""([^""]*)""$")]
    public void ThenHasDiscordUsername(string teamName, string discordUsername) =>
        _kept.GetValueOrDefault(teamName).ShouldNotBeNull().DiscordUsername.ShouldBe(discordUsername);

    [Then(@"^""([^""]*)"" has no contact details$")]
    public void ThenHasNone(string teamName) => _kept.ShouldNotContainKey(teamName);

    [Then(@"^the contact details are refused$")]
    public void ThenRefused() => _refusal.ShouldNotBeNull();

    private void Record(string person, string teamName, string? email, string? phone, string? discordUsername)
    {
        var league = world.RequireLeague();
        var member = league.Members.Single(m => m.TeamName == teamName);

        try
        {
            var details = league.RecordContactDetails(
                new RecordContactDetails(member.MemberId, ImportWorld.Subject(person), email, phone, discordUsername));

            if (details == ContactDetails.None)
            {
                _kept.Remove(teamName);
            }
            else
            {
                _kept[teamName] = details;
            }

            _refusal = null;
        }
        catch (DomainException refusal)
        {
            _refusal = refusal;
        }
    }

    private static string? Blank(string cell) => string.IsNullOrWhiteSpace(cell) ? null : cell;
}

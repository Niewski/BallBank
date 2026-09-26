using BallBank.Domain;
using BallBank.Domain.Membership;
using BallBank.Specs.Support;
using Reqnroll;

namespace BallBank.Specs.StepDefinitions;

/// <summary>Treasurers appointed in the league <see cref="ImportWorld"/> imported, each at the world's now.</summary>
[Binding]
public sealed class AppointingATreasurerSteps(ImportWorld world)
{
    private readonly List<TreasurerAppointed> _appointments = [];
    private DomainException? _refusal;

    [Given(@"^(\w+) has appointed ""([^""]*)"" as a treasurer$")]
    public void GivenAppointed(string person, string teamName)
    {
        WhenAppoints(person, teamName);
        _refusal.ShouldBeNull();
    }

    [When(@"^(\w+) appoints ""([^""]*)"" as a treasurer(?: again)?$")]
    public void WhenAppoints(string person, string teamName)
    {
        var league = world.RequireLeague();
        var member = league.Members.Single(m => m.TeamName == teamName);

        try
        {
            if (league.AppointTreasurer(new AppointTreasurer(member.MemberId, ImportWorld.Subject(person)), ImportWorld.Now) is { } appointed)
            {
                league.Evolve(appointed);
                _appointments.Add(appointed);
            }

            _refusal = null;
        }
        catch (DomainException refusal)
        {
            _refusal = refusal;
        }
    }

    [Then(@"^the appointment is refused$")]
    public void ThenRefused() => _refusal.ShouldNotBeNull();

    [Then(@"^(\w+) is not a treasurer$")]
    public void ThenIsNotATreasurer(string person) =>
        world.RequireLeague().MemberHeldBy(ImportWorld.Subject(person)).ShouldNotBeNull().IsTreasurer.ShouldBeFalse();

    [Then(@"^the history shows (\w+) appointed (\w+)$")]
    public void ThenHistoryShows(string appointer, string appointee)
    {
        var member = world.RequireLeague().MemberHeldBy(ImportWorld.Subject(appointee)).ShouldNotBeNull();
        _appointments.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            a => a.MemberId.ShouldBe(member.MemberId),
            a => a.AppointedBy.ShouldBe(ImportWorld.Subject(appointer)));
    }

    [Then(@"^""([^""]*)"" was appointed once$")]
    public void ThenAppointedOnce(string teamName)
    {
        var member = world.RequireLeague().Members.Single(m => m.TeamName == teamName);
        _appointments.Where(a => a.MemberId == member.MemberId).ShouldHaveSingleItem();
    }
}

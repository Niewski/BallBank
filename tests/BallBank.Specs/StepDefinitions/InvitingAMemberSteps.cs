using BallBank.Domain;
using BallBank.Domain.Membership;
using BallBank.Specs.Support;
using Reqnroll;

namespace BallBank.Specs.StepDefinitions;

/// <summary>Invites issued in the league <see cref="ImportWorld"/> imported, each at the world's now.</summary>
[Binding]
public sealed class InvitingAMemberSteps(ImportWorld world)
{
    private DomainException? _refusal;

    [Given(@"^(\w+) has invited ""([^""]*)""(?: again)?$")]
    public void GivenInvited(string person, string teamName)
    {
        WhenInvites(person, teamName);
        _refusal.ShouldBeNull();
    }

    [When(@"^(\w+) invites ""([^""]*)""(?: again)?$")]
    public void WhenInvites(string person, string teamName)
    {
        var member = world.RequireLeague().Members.Single(m => m.TeamName == teamName);
        Issue(new IssueInvite(Guid.NewGuid(), member.MemberId, ImportWorld.Subject(person)));
    }

    [When(@"^(\w+) sends that same invite again$")]
    public void WhenSendsAgain(string person) =>
        Issue(world.Invites[^1] with { IssuerSubject = ImportWorld.Subject(person) });

    [Then(@"^the invite for ""([^""]*)"" can be used for 14 days$")]
    public void ThenUsableFor14Days(string teamName)
    {
        var invite = InvitesFor(teamName).ShouldHaveSingleItem();
        var league = world.RequireLeague();

        league.IsInviteValid(invite.InviteId, ImportWorld.Now.AddDays(14).AddSeconds(-1)).ShouldBeTrue();
        league.IsInviteValid(invite.InviteId, ImportWorld.Now.AddDays(14)).ShouldBeFalse();
    }

    [Then(@"^only the newer invite for ""([^""]*)"" can be used$")]
    public void ThenOnlyTheNewer(string teamName)
    {
        var invites = InvitesFor(teamName);
        var league = world.RequireLeague();

        invites.Count.ShouldBe(2);
        league.IsInviteValid(invites[0].InviteId, ImportWorld.Now).ShouldBeFalse();
        league.IsInviteValid(invites[1].InviteId, ImportWorld.Now).ShouldBeTrue();
    }

    [Then(@"^the invite is refused$")]
    public void ThenRefused() => _refusal.ShouldNotBeNull();

    private void Issue(IssueInvite command)
    {
        var league = world.RequireLeague();

        try
        {
            if (league.IssueInvite(command, ImportWorld.Now) is { } issued)
            {
                league.Evolve(issued);
            }

            world.Invites.Add(command);
            _refusal = null;
        }
        catch (DomainException refusal)
        {
            _refusal = refusal;
        }
    }

    /// <summary>The distinct invites the league has for this team, oldest first.</summary>
    private List<Invite> InvitesFor(string teamName)
    {
        var league = world.RequireLeague();
        var member = league.Members.Single(m => m.TeamName == teamName);
        return world.Invites
            .Select(c => c.InviteId)
            .Distinct()
            .Select(league.InviteWithId)
            .OfType<Invite>()
            .Where(i => i.MemberId == member.MemberId)
            .ToList();
    }
}

using BallBank.Domain;
using BallBank.Domain.Membership;
using BallBank.Specs.Support;
using Reqnroll;

namespace BallBank.Specs.StepDefinitions;

/// <summary>Claims revoked in the league <see cref="ImportWorld"/> imported, each at the world's now.</summary>
[Binding]
public sealed class RevokingAClaimSteps(ImportWorld world)
{
    private DomainException? _refusal;

    [When(@"^(\w+) revokes (\w+)'s claim to ""([^""]*)"" because ""([^""]*)""$")]
    public void WhenRevokes(string revoker, string holder, string teamName, string reason)
    {
        var league = world.RequireLeague();
        var member = league.Members.Single(m => m.TeamName == teamName);

        try
        {
            var command = new RevokeClaim(member.MemberId, ImportWorld.Subject(holder), ImportWorld.Subject(revoker), reason);
            if (league.RevokeClaim(command, ImportWorld.Now) is { } revoked)
            {
                league.Evolve(revoked);
            }

            _refusal = null;
        }
        catch (DomainException refusal)
        {
            _refusal = refusal;
        }
    }

    [Then(@"^the revocation is refused$")]
    public void ThenRefused() => _refusal.ShouldNotBeNull();

    [Then(@"^(\w+) can invite someone else to ""([^""]*)""$")]
    public void ThenCanInvite(string person, string teamName)
    {
        var league = world.RequireLeague();
        var member = league.Members.Single(m => m.TeamName == teamName);

        league.IssueInvite(new IssueInvite(Guid.NewGuid(), member.MemberId, ImportWorld.Subject(person)), ImportWorld.Now)
            .ShouldNotBeNull();
    }
}

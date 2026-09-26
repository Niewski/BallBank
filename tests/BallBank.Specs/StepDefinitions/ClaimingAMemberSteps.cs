using BallBank.Domain;
using BallBank.Domain.Membership;
using BallBank.Specs.Support;
using Reqnroll;

namespace BallBank.Specs.StepDefinitions;

/// <summary>Claims made through the invites <see cref="InvitingAMemberSteps"/> issued, each at the world's now unless the step says later.</summary>
[Binding]
public sealed class ClaimingAMemberSteps(ImportWorld world)
{
    private readonly List<MemberClaimed> _claims = [];
    private DomainException? _refusal;

    [When(@"^(\w+) claims ""([^""]*)"" with the (invite|first invite)(?: again)?$")]
    public void WhenClaims(string person, string teamName, string which) => Claim(person, teamName, which, ImportWorld.Now);

    [When(@"^(\w+) claims ""([^""]*)"" with the (invite|first invite) (\d+) days later$")]
    public void WhenClaimsLater(string person, string teamName, string which, int days) =>
        Claim(person, teamName, which, ImportWorld.Now.AddDays(days));

    private void Claim(string person, string teamName, string which, DateTimeOffset now)
    {
        var league = world.RequireLeague();
        var member = league.Members.Single(m => m.TeamName == teamName);
        var invites = world.Invites.Where(i => i.MemberId == member.MemberId).ToList();
        var invite = which == "first invite" ? invites[0] : invites[^1];

        try
        {
            if (league.ClaimMember(new ClaimMember(member.MemberId, invite.InviteId, ImportWorld.Subject(person), person), now) is { } claimed)
            {
                league.Evolve(claimed);
                _claims.Add(claimed);
            }

            _refusal = null;
        }
        catch (DomainException refusal)
        {
            _refusal = refusal;
        }
    }

    [Then(@"^the claim is refused because the invite (has expired|was replaced)$")]
    public void ThenRefused(string reason) =>
        _refusal.ShouldNotBeNull().Message.ShouldContain(reason == "has expired" ? "expired" : "replaced");

    [Then(@"^""([^""]*)"" is unclaimed$")]
    public void ThenUnclaimed(string teamName) =>
        world.RequireLeague().Members.Single(m => m.TeamName == teamName).IsClaimed.ShouldBeFalse();

    [Then(@"^""([^""]*)"" was claimed once$")]
    public void ThenClaimedOnce(string teamName)
    {
        var member = world.RequireLeague().Members.Single(m => m.TeamName == teamName);
        _claims.Where(c => c.MemberId == member.MemberId).ShouldHaveSingleItem();
    }
}

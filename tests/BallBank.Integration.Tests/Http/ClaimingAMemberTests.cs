using System.Net;
using System.Net.Http.Json;
using BallBank.Api.Features.Membership;
using BallBank.Domain.Membership;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace BallBank.Integration.Tests.Http;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class ClaimingAMemberTests(PostgresFixture postgres)
{
    private BallBankApi Api => postgres.Api;
    private IDocumentStore Store => Api.Services.GetRequiredService<IDocumentStore>();

    [Fact]
    public async Task An_invited_person_claims_the_member_and_the_league_is_among_their_leagues()
    {
        var hogs = await HollandHogs.Invited(this);
        var priya = NewSubject();

        var response = await Claim(priya, hogs, new ClaimMemberRequest(hogs.InviteId, " Priya ", "priya@example.com"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var claimed = (await response.Content.ReadFromJsonAsync<ClaimedMember>()).ShouldNotBeNull();
        (claimed.LeagueId, claimed.Name, claimed.Season, claimed.MemberId, claimed.TeamName, claimed.DisplayName)
            .ShouldBe((hogs.LeagueId, "Holland Hogs", "2026", hogs.Priyas, "Priya", "Priya"));
        claimed.Roles.ShouldBeEmpty();

        await using var session = Store.QuerySession();
        var membership = (await session.LoadAsync<UserMemberships>(priya)).ShouldNotBeNull().Leagues.ShouldHaveSingleItem();
        (membership.LeagueId, membership.MemberId).ShouldBe((hogs.LeagueId, hogs.Priyas));

        var myLeagues = await Api.CreateClientFor(priya).GetFromJsonAsync<MyLeague[]>("/me/leagues");
        var myLeague = myLeagues.ShouldNotBeNull().ShouldHaveSingleItem();
        (myLeague.LeagueId, myLeague.Name, myLeague.MemberId).ShouldBe((hogs.LeagueId, "Holland Hogs", hogs.Priyas));
    }

    [Fact]
    public async Task The_member_list_shows_the_member_claimed_by_its_holder_with_their_contact_details()
    {
        var hogs = await HollandHogs.Invited(this);
        var priya = NewSubject();

        (await Claim(priya, hogs, new ClaimMemberRequest(hogs.InviteId, "Priya P", "priya@example.com", "(555) 010-0003", "@PriyaP")))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var members = await Api.CreateClientFor(hogs.Jacob).GetFromJsonAsync<LeagueMembers>($"/leagues/{hogs.LeagueId}/members");
        var priyas = members.ShouldNotBeNull().Members.Single(m => m.MemberId == hogs.Priyas);
        priyas.Claimed.ShouldBeTrue();
        priyas.HolderDisplayName.ShouldBe("Priya P");
        priyas.Contact.ShouldBe(new ContactDetails("priya@example.com", "+15550100003", "priyap"));
    }

    [Fact]
    public async Task An_expired_invite_is_gone()
    {
        var hogs = await HollandHogs.Invited(this);
        var expired = await IssuedLongAgo(hogs);

        var response = await Claim(NewSubject(), hogs, new ClaimMemberRequest(expired, "Priya", "priya@example.com"));

        response.StatusCode.ShouldBe(HttpStatusCode.Gone);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>()).ShouldNotBeNull()
            .Detail.ShouldBe("This invite has expired. Ask the treasurer for a new one.");
        (await LeagueAsync(hogs)).Members.Single(m => m.MemberId == hogs.Priyas).IsClaimed.ShouldBeFalse();
    }

    [Fact]
    public async Task An_invite_a_newer_one_replaced_is_gone()
    {
        var hogs = await HollandHogs.Invited(this);
        await Invite(hogs.Jacob, hogs.LeagueId, hogs.Priyas);

        var response = await Claim(NewSubject(), hogs, new ClaimMemberRequest(hogs.InviteId, "Priya", "priya@example.com"));

        response.StatusCode.ShouldBe(HttpStatusCode.Gone);
        (await LeagueAsync(hogs)).Members.Single(m => m.MemberId == hogs.Priyas).IsClaimed.ShouldBeFalse();
    }

    [Fact]
    public async Task Someone_who_already_holds_a_member_here_is_refused()
    {
        // Jacob opens the link he made for Priya.
        var hogs = await HollandHogs.Invited(this);

        var response = await Claim(hogs.Jacob, hogs, new ClaimMemberRequest(hogs.InviteId, "Jacob", "jacob@example.com"));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await LeagueAsync(hogs)).Members.Single(m => m.MemberId == hogs.Priyas).IsClaimed.ShouldBeFalse();
    }

    [Fact]
    public async Task A_member_someone_else_claimed_is_refused()
    {
        var hogs = await HollandHogs.Invited(this);
        (await Claim(NewSubject(), hogs, new ClaimMemberRequest(hogs.InviteId, "Priya", "priya@example.com")))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await Claim(NewSubject(), hogs, new ClaimMemberRequest(hogs.InviteId, "Dana", "dana@example.com"));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Claiming_twice_claims_once()
    {
        var hogs = await HollandHogs.Invited(this);
        var priya = NewSubject();
        var request = new ClaimMemberRequest(hogs.InviteId, "Priya", "priya@example.com");

        (await Claim(priya, hogs, request)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Claim(priya, hogs, request)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        (await session.Events.FetchStreamAsync(hogs.LeagueId)).Count(e => e.Data is MemberClaimed { InviteId: not null }).ShouldBe(1);
        await using var global = Store.QuerySession();
        (await global.LoadAsync<UserMemberships>(priya)).ShouldNotBeNull().Leagues.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_claim_without_an_email_is_a_bad_request_and_claims_nothing()
    {
        var hogs = await HollandHogs.Invited(this);

        var response = await Claim(NewSubject(), hogs, new ClaimMemberRequest(hogs.InviteId, "Priya", Phone: "(555) 010-0003"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await LeagueAsync(hogs)).Members.Single(m => m.MemberId == hogs.Priyas).IsClaimed.ShouldBeFalse();
    }

    [Fact]
    public async Task An_invite_the_league_never_issued_is_not_found()
    {
        var hogs = await HollandHogs.Invited(this);

        var response = await Claim(NewSubject(), hogs, new ClaimMemberRequest(Guid.NewGuid(), "Priya", "priya@example.com"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Without_a_token_the_caller_is_unauthorized()
    {
        var response = await Api.CreateClient().PostAsJsonAsync(
            $"/leagues/{Guid.NewGuid()}/claims",
            new ClaimMemberRequest(Guid.NewGuid(), "Priya", "priya@example.com"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Opening_an_invite_shows_the_league_the_team_and_what_the_treasurer_recorded()
    {
        var hogs = await HollandHogs.Invited(this);
        (await Api.CreateClientFor(hogs.Jacob).PutAsJsonAsync(
            $"/leagues/{hogs.LeagueId}/members/{hogs.Priyas}/contact",
            new ContactDetailsRequest(Phone: "555-010-0003"))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var invite = await Open(NewSubject(), hogs, hogs.InviteId);

        invite.ShouldBe(new InviteToClaim(
            hogs.LeagueId,
            "Holland Hogs",
            "2026",
            hogs.Priyas,
            "Priya",
            "Priya",
            Yours: false,
            Refusal: null,
            new ContactDetails(null, "+15550100003", null)));
    }

    [Fact]
    public async Task Opening_a_replaced_invite_says_why_it_cannot_be_used_and_shows_no_contact_details()
    {
        var hogs = await HollandHogs.Invited(this);
        await Invite(hogs.Jacob, hogs.LeagueId, hogs.Priyas);

        var invite = await Open(NewSubject(), hogs, hogs.InviteId);

        invite.Refusal.ShouldBe("This invite was replaced by a newer one. Use the latest link the treasurer sent.");
        invite.Contact.ShouldBeNull();
    }

    [Fact]
    public async Task Opening_an_invite_while_holding_another_member_says_why_it_cannot_be_used()
    {
        var hogs = await HollandHogs.Invited(this);

        var invite = await Open(hogs.Jacob, hogs, hogs.InviteId);

        invite.Refusal.ShouldBe("You already hold Hog Wild in Holland Hogs, and one person holds one member per league.");
        invite.Contact.ShouldBeNull();
    }

    [Fact]
    public async Task Opening_an_invite_already_used_to_claim_shows_the_member_is_yours()
    {
        var hogs = await HollandHogs.Invited(this);
        var priya = NewSubject();
        (await Claim(priya, hogs, new ClaimMemberRequest(hogs.InviteId, "Priya", "priya@example.com")))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var invite = await Open(priya, hogs, hogs.InviteId);

        invite.Yours.ShouldBeTrue();
        invite.Refusal.ShouldBeNull();
    }

    [Fact]
    public async Task Opening_an_invite_the_league_never_issued_is_not_found()
    {
        var hogs = await HollandHogs.Invited(this);

        var response = await Api.CreateClientFor(NewSubject()).GetAsync($"/leagues/{hogs.LeagueId}/invites/{Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<InviteToClaim> Open(string subject, HollandHogs hogs, Guid inviteId) =>
        (await Api.CreateClientFor(subject).GetFromJsonAsync<InviteToClaim>($"/leagues/{hogs.LeagueId}/invites/{inviteId}"))
            .ShouldNotBeNull();

    // BallBank's clock cannot be moved, so the invite is written as if Jacob had issued it 15 days ago.
    private async Task<Guid> IssuedLongAgo(HollandHogs hogs)
    {
        var inviteId = Guid.NewGuid();
        var issuedAt = DateTimeOffset.UtcNow.AddDays(-15);
        await using var session = Store.LightweightSession(hogs.LeagueId.ToString());
        session.Events.Append(hogs.LeagueId, new InviteIssued(inviteId, hogs.Priyas, hogs.Jacob, issuedAt + League.InviteLifetime, issuedAt));
        await session.SaveChangesAsync();
        return inviteId;
    }

    private async Task<League> LeagueAsync(HollandHogs hogs)
    {
        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        return (await session.Events.AggregateStreamAsync<League>(hogs.LeagueId)).ShouldNotBeNull();
    }

    private Task<HttpResponseMessage> Claim(string subject, HollandHogs hogs, ClaimMemberRequest request) =>
        Api.CreateClientFor(subject).PostAsJsonAsync($"/leagues/{hogs.LeagueId}/claims", request);

    /// <summary>Holland Hogs as imported by Jacob, who keeps its books, with an invite issued for Priya's member.</summary>
    private sealed record HollandHogs(Guid LeagueId, string Jacob, Guid Priyas, Guid InviteId)
    {
        public static async Task<HollandHogs> Invited(ClaimingAMemberTests tests)
        {
            var leagueId = Guid.NewGuid();
            var jacob = NewSubject();
            var jacobs = tests.Api.CreateClientFor(jacob);

            var imported = await jacobs.PostAsJsonAsync(
                $"/leagues/{leagueId}/import",
                new ImportLeagueRequest(tests.Api.Sleeper.CopyOfHollandHogs(), "jacob", "Jacob"));
            imported.StatusCode.ShouldBe(HttpStatusCode.Created);

            var members = (await jacobs.GetFromJsonAsync<LeagueMembers>($"/leagues/{leagueId}/members")).ShouldNotBeNull();
            var priyas = members.Members.Single(m => m.TeamName == "Priya").MemberId;

            var inviteId = await tests.Invite(jacob, leagueId, priyas);
            return new HollandHogs(leagueId, jacob, priyas, inviteId);
        }
    }

    private async Task<Guid> Invite(string treasurer, Guid leagueId, Guid memberId)
    {
        var inviteId = Guid.NewGuid();
        var response = await Api.CreateClientFor(treasurer).PostAsJsonAsync(
            $"/leagues/{leagueId}/members/{memberId}/invites",
            new IssueInviteRequest(inviteId));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return inviteId;
    }

    private static string NewSubject() => $"test|{Guid.NewGuid():N}";
}

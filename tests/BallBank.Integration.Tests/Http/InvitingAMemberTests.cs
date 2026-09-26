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
public class InvitingAMemberTests(PostgresFixture postgres)
{
    private BallBankApi Api => postgres.Api;
    private IDocumentStore Store => Api.Services.GetRequiredService<IDocumentStore>();

    [Fact]
    public async Task A_treasurer_invites_a_member_with_a_claim_link_that_expires_in_14_days()
    {
        var hogs = await HollandHogs.Import(this);
        var inviteId = Guid.NewGuid();
        var before = DateTimeOffset.UtcNow;

        var response = await Invite(hogs.Jacob, hogs, hogs.Priyas, new IssueInviteRequest(inviteId));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var invite = (await response.Content.ReadFromJsonAsync<IssuedInvite>()).ShouldNotBeNull();
        invite.InviteId.ShouldBe(inviteId);
        invite.MemberId.ShouldBe(hogs.Priyas);
        invite.Url.ShouldBe($"/claim?league={hogs.LeagueId}&invite={inviteId}");
        invite.ExpiresAt.ShouldBeInRange(before.AddDays(14), DateTimeOffset.UtcNow.AddDays(14));
        (await LeagueAsync(hogs)).IsInviteValid(inviteId, DateTimeOffset.UtcNow).ShouldBeTrue();
    }

    [Fact]
    public async Task An_invite_without_an_id_is_a_bad_request()
    {
        // The client names the invite, so a retried request is recognised rather than issuing another.
        var hogs = await HollandHogs.Import(this);

        var response = await Api.CreateClientFor(hogs.Jacob).PostAsJsonAsync(
            $"/leagues/{hogs.LeagueId}/members/{hogs.Priyas}/invites",
            new { });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        (await session.Events.FetchStreamAsync(hogs.LeagueId)).ShouldNotContain(e => e.Data is InviteIssued);
    }

    [Fact]
    public async Task A_new_invite_voids_the_earlier_one()
    {
        var hogs = await HollandHogs.Import(this);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        (await Invite(hogs.Jacob, hogs, hogs.Priyas, new IssueInviteRequest(first))).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await Invite(hogs.Jacob, hogs, hogs.Priyas, new IssueInviteRequest(second))).StatusCode.ShouldBe(HttpStatusCode.Created);

        var league = await LeagueAsync(hogs);
        league.IsInviteValid(first, DateTimeOffset.UtcNow).ShouldBeFalse();
        league.IsInviteValid(second, DateTimeOffset.UtcNow).ShouldBeTrue();
    }

    [Fact]
    public async Task Sending_the_same_invite_again_answers_with_the_same_invite_and_issues_nothing_new()
    {
        var hogs = await HollandHogs.Import(this);
        var request = new IssueInviteRequest(Guid.NewGuid());
        var first = await (await Invite(hogs.Jacob, hogs, hogs.Priyas, request)).Content.ReadFromJsonAsync<IssuedInvite>();

        var response = await Invite(hogs.Jacob, hogs, hogs.Priyas, request);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<IssuedInvite>()).ShouldBe(first);
        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        (await session.Events.FetchStreamAsync(hogs.LeagueId)).Count(e => e.Data is InviteIssued).ShouldBe(1);
    }

    [Fact]
    public async Task A_claimed_member_cannot_be_invited()
    {
        var hogs = await HollandHogs.Import(this);

        var response = await Invite(hogs.Jacob, hogs, hogs.Sams, new IssueInviteRequest(Guid.NewGuid()));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>()).ShouldNotBeNull()
            .Detail.ShouldBe("Sam's Slammers is already claimed by Sam. Revoke that claim before inviting someone else.");
    }

    [Fact]
    public async Task A_member_who_is_not_a_treasurer_is_forbidden_to_invite()
    {
        var hogs = await HollandHogs.Import(this);
        var inviteId = Guid.NewGuid();

        var response = await Invite(hogs.Sam, hogs, hogs.Priyas, new IssueInviteRequest(inviteId));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await LeagueAsync(hogs)).InviteWithId(inviteId).ShouldBeNull();
    }

    [Fact]
    public async Task Someone_outside_the_league_is_forbidden_to_invite()
    {
        var hogs = await HollandHogs.Import(this);

        var response = await Invite(NewSubject(), hogs, hogs.Priyas, new IssueInviteRequest(Guid.NewGuid()));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Without_a_token_the_caller_is_unauthorized()
    {
        var response = await Api.CreateClient().PostAsJsonAsync(
            $"/leagues/{Guid.NewGuid()}/members/{Guid.NewGuid()}/invites",
            new IssueInviteRequest(Guid.NewGuid()));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_member_the_league_does_not_have_is_not_found()
    {
        var hogs = await HollandHogs.Import(this);

        var response = await Invite(hogs.Jacob, hogs, Guid.NewGuid(), new IssueInviteRequest(Guid.NewGuid()));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private Task<HttpResponseMessage> Invite(string subject, HollandHogs hogs, Guid memberId, IssueInviteRequest request) =>
        Api.CreateClientFor(subject).PostAsJsonAsync($"/leagues/{hogs.LeagueId}/members/{memberId}/invites", request);

    private async Task<League> LeagueAsync(HollandHogs hogs)
    {
        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        return (await session.Events.AggregateStreamAsync<League>(hogs.LeagueId)).ShouldNotBeNull();
    }

    /// <summary>Holland Hogs as imported by Jacob, who keeps its books, with Sam holding Sam's Slammers and Priya holding nothing yet.</summary>
    private sealed record HollandHogs(Guid LeagueId, string Jacob, string Sam, Guid Sams, Guid Priyas)
    {
        public static async Task<HollandHogs> Import(InvitingAMemberTests tests)
        {
            var leagueId = Guid.NewGuid();
            var jacob = NewSubject();
            var response = await tests.Api.CreateClientFor(jacob).PostAsJsonAsync(
                $"/leagues/{leagueId}/import",
                new ImportLeagueRequest(tests.Api.Sleeper.CopyOfHollandHogs(), "jacob", "Jacob"));
            response.StatusCode.ShouldBe(HttpStatusCode.Created);

            var sam = NewSubject();
            var league = await tests.ClaimAsync(leagueId, rosterId: 2, sam, "Sam");

            Guid MemberOf(int rosterId) => league.Members.Single(m => m.SleeperRosterId == rosterId).MemberId;
            return new HollandHogs(leagueId, jacob, sam, MemberOf(2), MemberOf(3));
        }
    }

    // Written the way a claim writes it, without the invite and the contact details a claim over HTTP needs.
    private async Task<League> ClaimAsync(Guid leagueId, int rosterId, string subject, string displayName)
    {
        await using var session = Store.LightweightSession(leagueId.ToString());
        var league = (await session.Events.AggregateStreamAsync<League>(leagueId)).ShouldNotBeNull();
        var member = league.Members.Single(m => m.SleeperRosterId == rosterId);
        var claimed = new MemberClaimed(member.MemberId, subject, displayName, Guid.NewGuid(), DateTimeOffset.UtcNow);
        session.Events.Append(leagueId, claimed);
        session.Store(new UserMemberships
        {
            Id = subject,
            Leagues = [new LeagueMembership(leagueId, league.Name, league.Season, member.MemberId, [])],
        });
        await session.SaveChangesAsync();

        league.Evolve(claimed);
        return league;
    }

    private static string NewSubject() => $"test|{Guid.NewGuid():N}";
}

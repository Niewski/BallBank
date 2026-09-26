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
public class RevokingAClaimTests(PostgresFixture postgres)
{
    private BallBankApi Api => postgres.Api;
    private IDocumentStore Store => Api.Services.GetRequiredService<IDocumentStore>();

    [Fact]
    public async Task A_treasurer_revokes_a_claim_and_the_member_is_unclaimed()
    {
        var hogs = await HollandHogs.Import(this);

        var response = await Revoke(hogs.Jacob, hogs, hogs.Sams, hogs.Sam, "Wrong person claimed it");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await LeagueAsync(hogs)).Members.Single(m => m.MemberId == hogs.Sams).IsClaimed.ShouldBeFalse();
    }

    [Fact]
    public async Task The_history_records_who_revoked_the_claim_and_why()
    {
        var hogs = await HollandHogs.Import(this);

        await Revoke(hogs.Jacob, hogs, hogs.Sams, hogs.Sam, "Wrong person claimed it");

        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        var revoked = (await session.Events.FetchStreamAsync(hogs.LeagueId))
            .Select(e => e.Data).OfType<MemberClaimRevoked>()
            .Single(r => r.MemberId == hogs.Sams);
        (revoked.Subject, revoked.RevokedBy, revoked.Reason).ShouldBe((hogs.Sam, hogs.Jacob, "Wrong person claimed it"));
    }

    [Fact]
    public async Task The_revoked_subjects_memberships_no_longer_list_the_league_and_their_requests_are_forbidden()
    {
        var hogs = await HollandHogs.Import(this);

        (await Revoke(hogs.Jacob, hogs, hogs.Sams, hogs.Sam, "Wrong person claimed it"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var session = Store.QuerySession();
        (await session.LoadAsync<UserMemberships>(hogs.Sam)).ShouldNotBeNull().Leagues.ShouldBeEmpty();

        var response = await Api.CreateClientFor(hogs.Sam).GetAsync($"/leagues/{hogs.LeagueId}/members");
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_member_can_be_invited_again_after_its_claim_is_revoked()
    {
        var hogs = await HollandHogs.Import(this);
        await Revoke(hogs.Jacob, hogs, hogs.Sams, hogs.Sam, "Wrong person claimed it");

        var response = await Api.CreateClientFor(hogs.Jacob).PostAsJsonAsync(
            $"/leagues/{hogs.LeagueId}/members/{hogs.Sams}/invites",
            new IssueInviteRequest(Guid.NewGuid()));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Revoking_the_only_treasurers_claim_is_refused()
    {
        var hogs = await HollandHogs.Import(this);

        var response = await Revoke(hogs.Jacob, hogs, hogs.Jacobs, hogs.Jacob, "Stepping down");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await LeagueAsync(hogs)).Members.Single(m => m.MemberId == hogs.Jacobs).IsClaimed.ShouldBeTrue();
    }

    [Fact]
    public async Task A_member_who_is_not_a_treasurer_is_forbidden_to_revoke()
    {
        var hogs = await HollandHogs.Import(this);

        var response = await Revoke(hogs.Sam, hogs, hogs.Sams, hogs.Sam, "Wrong person claimed it");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await LeagueAsync(hogs)).Members.Single(m => m.MemberId == hogs.Sams).IsClaimed.ShouldBeTrue();
    }

    [Fact]
    public async Task Revoking_an_unclaimed_members_claim_is_a_no_op()
    {
        var hogs = await HollandHogs.Import(this);

        var response = await Revoke(hogs.Jacob, hogs, hogs.Priyas, "test|nobody", "Never claimed");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Without_a_token_the_caller_is_unauthorized()
    {
        var response = await Api.CreateClient().SendAsync(new HttpRequestMessage(
            HttpMethod.Delete, $"/leagues/{Guid.NewGuid()}/members/{Guid.NewGuid()}/claims/test|someone")
        {
            Content = JsonContent.Create(new RevokeClaimRequest("Because")),
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_member_the_league_does_not_have_is_not_found()
    {
        var hogs = await HollandHogs.Import(this);

        var response = await Revoke(hogs.Jacob, hogs, Guid.NewGuid(), hogs.Sam, "Wrong person claimed it");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private Task<HttpResponseMessage> Revoke(string caller, HollandHogs hogs, Guid memberId, string subject, string reason)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Delete, $"/leagues/{hogs.LeagueId}/members/{memberId}/claims/{Uri.EscapeDataString(subject)}")
        {
            Content = JsonContent.Create(new RevokeClaimRequest(reason)),
        };
        return Api.CreateClientFor(caller).SendAsync(request);
    }

    private async Task<League> LeagueAsync(HollandHogs hogs)
    {
        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        return (await session.Events.AggregateStreamAsync<League>(hogs.LeagueId)).ShouldNotBeNull();
    }

    /// <summary>Holland Hogs as imported by Jacob, who keeps its books, with Sam holding Sam's Slammers.</summary>
    private sealed record HollandHogs(Guid LeagueId, string Jacob, Guid Jacobs, string Sam, Guid Sams, Guid Priyas)
    {
        public static async Task<HollandHogs> Import(RevokingAClaimTests tests)
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
            return new HollandHogs(leagueId, jacob, MemberOf(1), sam, MemberOf(2), MemberOf(3));
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

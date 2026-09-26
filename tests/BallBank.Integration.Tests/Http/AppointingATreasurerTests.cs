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
public class AppointingATreasurerTests(PostgresFixture postgres)
{
    private BallBankApi Api => postgres.Api;
    private IDocumentStore Store => Api.Services.GetRequiredService<IDocumentStore>();

    [Fact]
    public async Task A_treasurer_appoints_a_claimed_member_who_then_sees_the_role_in_my_leagues()
    {
        var hogs = await HollandHogs.Import(this);

        var response = await Appoint(hogs.Jacob, hogs, hogs.Sams);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await LeagueAsync(hogs)).Members.Single(m => m.MemberId == hogs.Sams).IsTreasurer.ShouldBeTrue();
        var sams = (await Api.CreateClientFor(hogs.Sam).GetFromJsonAsync<MyLeague[]>("/me/leagues")).ShouldNotBeNull();
        sams.ShouldHaveSingleItem().Roles.ShouldBe([Roles.Treasurer]);
    }

    [Fact]
    public async Task The_history_records_who_made_the_appointment()
    {
        var hogs = await HollandHogs.Import(this);

        await Appoint(hogs.Jacob, hogs, hogs.Sams);

        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        (await session.Events.FetchStreamAsync(hogs.LeagueId))
            .Select(e => e.Data).OfType<TreasurerAppointed>()
            .Single(a => a.MemberId == hogs.Sams)
            .AppointedBy.ShouldBe(hogs.Jacob);
    }

    [Fact]
    public async Task An_appointed_treasurer_can_act_as_one()
    {
        var hogs = await HollandHogs.Import(this);
        await Appoint(hogs.Jacob, hogs, hogs.Sams);

        var response = await Api.CreateClientFor(hogs.Sam).PostAsJsonAsync(
            $"/leagues/{hogs.LeagueId}/members/{hogs.Priyas}/invites",
            new IssueInviteRequest(Guid.NewGuid()));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Appointing_twice_appoints_once()
    {
        var hogs = await HollandHogs.Import(this);

        (await Appoint(hogs.Jacob, hogs, hogs.Sams)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Appoint(hogs.Jacob, hogs, hogs.Sams)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        (await session.Events.FetchStreamAsync(hogs.LeagueId))
            .Count(e => e.Data is TreasurerAppointed a && a.MemberId == hogs.Sams).ShouldBe(1);
        var sams = await session.LoadAsync<UserMemberships>(hogs.Sam);
        sams.ShouldNotBeNull().Leagues.ShouldHaveSingleItem().Roles.ShouldBe([Roles.Treasurer]);
    }

    [Fact]
    public async Task A_member_nobody_has_claimed_cannot_be_appointed()
    {
        var hogs = await HollandHogs.Import(this);

        var response = await Appoint(hogs.Jacob, hogs, hogs.Priyas);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>()).ShouldNotBeNull()
            .Detail.ShouldBe("Priya is not claimed yet, and only a claimed member can be a treasurer. Invite them first.");
        (await LeagueAsync(hogs)).Members.Single(m => m.MemberId == hogs.Priyas).IsTreasurer.ShouldBeFalse();
    }

    [Fact]
    public async Task A_member_who_is_not_a_treasurer_is_forbidden_to_appoint()
    {
        var hogs = await HollandHogs.Import(this);

        var response = await Appoint(hogs.Sam, hogs, hogs.Sams);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await LeagueAsync(hogs)).Members.Single(m => m.MemberId == hogs.Sams).IsTreasurer.ShouldBeFalse();
    }

    [Fact]
    public async Task Someone_outside_the_league_is_forbidden_to_appoint()
    {
        var hogs = await HollandHogs.Import(this);

        var response = await Appoint(NewSubject(), hogs, hogs.Sams);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Without_a_token_the_caller_is_unauthorized()
    {
        var response = await Api.CreateClient().PostAsJsonAsync(
            $"/leagues/{Guid.NewGuid()}/treasurers",
            new AppointTreasurerRequest(Guid.NewGuid()));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_member_the_league_does_not_have_is_not_found()
    {
        var hogs = await HollandHogs.Import(this);

        var response = await Appoint(hogs.Jacob, hogs, Guid.NewGuid());

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private Task<HttpResponseMessage> Appoint(string subject, HollandHogs hogs, Guid memberId) =>
        Api.CreateClientFor(subject).PostAsJsonAsync($"/leagues/{hogs.LeagueId}/treasurers", new AppointTreasurerRequest(memberId));

    private async Task<League> LeagueAsync(HollandHogs hogs)
    {
        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        return (await session.Events.AggregateStreamAsync<League>(hogs.LeagueId)).ShouldNotBeNull();
    }

    /// <summary>Holland Hogs as imported by Jacob, who keeps its books, with Sam holding Sam's Slammers and Priya holding nothing yet.</summary>
    private sealed record HollandHogs(Guid LeagueId, string Jacob, string Sam, Guid Sams, Guid Priyas)
    {
        public static async Task<HollandHogs> Import(AppointingATreasurerTests tests)
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

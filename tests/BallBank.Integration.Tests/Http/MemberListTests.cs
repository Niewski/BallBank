using System.Net;
using System.Net.Http.Json;
using BallBank.Api.Features.Membership;

namespace BallBank.Integration.Tests.Http;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class MemberListTests(PostgresFixture postgres)
{
    private BallBankApi Api => postgres.Api;

    [Fact]
    public async Task A_member_sees_every_member_of_their_league()
    {
        var jacob = NewSubject();
        var leagueId = await ImportHollandHogs(jacob);

        var list = await Api.CreateClientFor(jacob).GetFromJsonAsync<LeagueMembers>($"/leagues/{leagueId}/members");

        list.ShouldNotBeNull();
        list.LeagueId.ShouldBe(leagueId);
        list.Name.ShouldBe("Holland Hogs");
        list.Season.ShouldBe("2026");
        list.Members.Select(m => (m.TeamName, m.SleeperDisplayName, m.Claimed, m.HolderDisplayName, m.SuggestedTreasurer, string.Join(",", m.Roles)))
            .ShouldBe(
            [
                ("Hog Wild", "Jacob", true, "Jacob", true, Roles.Treasurer),
                ("Priya", "Priya", false, null, false, ""),
                ("Sam's Slammers", "Sam", false, null, false, ""),
                ("Team 4", null, false, null, false, ""),
            ]);
        list.Members.Select(m => m.MemberId).ShouldBeUnique();
    }

    [Fact]
    public async Task A_member_sees_which_member_is_theirs()
    {
        var jacob = NewSubject();
        var leagueId = await ImportHollandHogs(jacob);

        var list = await Api.CreateClientFor(jacob).GetFromJsonAsync<LeagueMembers>($"/leagues/{leagueId}/members");

        list.ShouldNotBeNull();
        list.Members.Single(m => m.MemberId == list.YourMemberId).TeamName.ShouldBe("Hog Wild");
    }

    [Fact]
    public async Task A_member_of_one_league_is_forbidden_another_leagues_members()
    {
        var jacob = NewSubject();
        await ImportHollandHogs(jacob);
        var someoneElsesLeague = await ImportHollandHogs(NewSubject());

        var response = await Api.CreateClientFor(jacob).GetAsync($"/leagues/{someoneElsesLeague}/members");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Someone_in_no_league_is_forbidden_a_league_that_exists()
    {
        var leagueId = await ImportHollandHogs(NewSubject());

        var response = await Api.CreateClientFor(NewSubject()).GetAsync($"/leagues/{leagueId}/members");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_guessed_league_id_is_forbidden_just_like_a_real_one()
    {
        var response = await Api.CreateClientFor(NewSubject()).GetAsync($"/leagues/{Guid.NewGuid()}/members");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_league_id_may_be_spelled_in_capitals()
    {
        var jacob = NewSubject();
        var leagueId = await ImportHollandHogs(jacob);

        var list = await Api.CreateClientFor(jacob)
            .GetFromJsonAsync<LeagueMembers>($"/leagues/{leagueId.ToString().ToUpperInvariant()}/members");

        list.ShouldNotBeNull();
        list.Members.Count.ShouldBe(4);
    }

    [Fact]
    public async Task Without_a_token_the_caller_is_unauthorized()
    {
        var response = await Api.CreateClient().GetAsync($"/leagues/{Guid.NewGuid()}/members");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(true, HttpStatusCode.OK)]
    [InlineData(false, HttpStatusCode.Forbidden)]
    public async Task A_request_under_a_league_carries_the_league_on_its_trace_and_logs(bool asMember, HttpStatusCode answered)
    {
        var jacob = NewSubject();
        var leagueId = await ImportHollandHogs(jacob);
        using var traced = new TracedRequest();

        var response = await Api.CreateClientFor(asMember ? jacob : NewSubject())
            .SendAsync(traced.Get($"/leagues/{leagueId.ToString().ToUpperInvariant()}/members"));

        response.StatusCode.ShouldBe(answered);
        (await traced.ServerSpan()).GetTagItem("tenant.id").ShouldBe(leagueId.ToString());

        var handling = Api.Logs.UnderTrace(traced.TraceId).Where(l => !BeforeRouting(l.Category)).ToList();
        handling.ShouldContain(l => l.Category.StartsWith("Microsoft.AspNetCore.Authorization"));
        handling.ShouldAllBe(l => Equals(l.Scope.GetValueOrDefault("tenant.id"), leagueId.ToString()));
    }

    [Fact]
    public async Task A_request_outside_any_league_carries_no_league()
    {
        using var traced = new TracedRequest();

        var response = await Api.CreateClientFor(NewSubject()).SendAsync(traced.Get("/me/leagues"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await traced.ServerSpan()).GetTagItem("tenant.id").ShouldBeNull();
        Api.Logs.UnderTrace(traced.TraceId).ShouldNotContain(l => l.Scope.ContainsKey("tenant.id"));
    }

    // What the host logs before routing has found the league in the path (and, for the host, after the response).
    private static bool BeforeRouting(string category) =>
        category.StartsWith("Microsoft.AspNetCore.Hosting", StringComparison.Ordinal)
        || category.StartsWith("Microsoft.AspNetCore.HostFiltering", StringComparison.Ordinal)
        || category.StartsWith("Microsoft.AspNetCore.Routing.Matching", StringComparison.Ordinal)
        || category == "Microsoft.AspNetCore.Routing.EndpointRoutingMiddleware";

    private async Task<Guid> ImportHollandHogs(string subject)
    {
        var leagueId = Guid.NewGuid();
        var response = await Api.CreateClientFor(subject).PostAsJsonAsync(
            $"/leagues/{leagueId}/import",
            new ImportLeagueRequest(Api.Sleeper.CopyOfHollandHogs(), "jacob", "Jacob"));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return leagueId;
    }

    private static string NewSubject() => $"test|{Guid.NewGuid():N}";
}

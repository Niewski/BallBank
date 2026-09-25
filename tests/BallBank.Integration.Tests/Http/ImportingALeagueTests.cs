using System.Net;
using System.Net.Http.Json;
using BallBank.Api.Features.Membership;
using BallBank.Domain.Membership;
using BallBank.Integration.Tests.Sleeper;
using JasperFx.Events;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace BallBank.Integration.Tests.Http;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class ImportingALeagueTests(PostgresFixture postgres)
{
    private BallBankApi Api => postgres.Api;

    private IDocumentStore Store => Api.Services.GetRequiredService<IDocumentStore>();

    [Fact]
    public async Task The_importer_becomes_the_treasurer_and_sees_the_league_in_my_leagues()
    {
        var subject = NewSubject();
        var client = Api.CreateClientFor(subject);
        var leagueId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            $"/leagues/{leagueId}/import",
            new ImportLeagueRequest(Api.Sleeper.CopyOfHollandHogs(), "jacob", "Jacob"));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var imported = await response.Content.ReadFromJsonAsync<ImportedLeague>();
        imported.ShouldNotBeNull();
        imported.LeagueId.ShouldBe(leagueId);
        imported.Name.ShouldBe("Holland Hogs");
        imported.Season.ShouldBe("2026");
        imported.Members.ShouldBe(4);
        imported.Roles.ShouldBe([Roles.Treasurer]);

        var myLeagues = await client.GetFromJsonAsync<MyLeague[]>("/me/leagues");
        myLeagues.ShouldNotBeNull();
        myLeagues.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            l => l.LeagueId.ShouldBe(leagueId),
            l => l.Name.ShouldBe("Holland Hogs"),
            l => l.Season.ShouldBe("2026"),
            l => l.MemberId.ShouldBe(imported.MemberId),
            l => l.Roles.ShouldBe([Roles.Treasurer]));
    }

    [Fact]
    public async Task The_league_its_index_the_importers_membership_and_the_Sleeper_responses_are_all_written()
    {
        var subject = NewSubject();
        var leagueId = Guid.NewGuid();
        var sleeperLeagueId = Api.Sleeper.CopyOfHollandHogs();

        var response = await Import(subject, leagueId, sleeperLeagueId);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        await using var session = Store.QuerySession(leagueId.ToString());

        // Alex co-owns Sam's team on Sleeper and is not a member of their own (ADR-0010).
        var league = await session.Events.AggregateStreamAsync<League>(leagueId);
        league.ShouldNotBeNull();
        league.SleeperLeagueId.ShouldBe(sleeperLeagueId);
        league.Members.Select(m => (m.SleeperRosterId, m.TeamName, m.SleeperDisplayName, m.SuggestedTreasurer, m.IsClaimed)).ShouldBe(
        [
            (1, "Hog Wild", "Jacob", true, true),
            (2, "Sam's Slammers", "Sam", false, false),
            (3, "Priya", "Priya", false, false),
            (4, "Team 4", null, false, false),
        ],
        ignoreOrder: true);

        var index = await session.LoadAsync<SleeperLeagueIndex>(sleeperLeagueId);
        index.ShouldNotBeNull();
        index.LeagueId.ShouldBe(leagueId);

        var memberships = await session.LoadAsync<UserMemberships>(subject);
        memberships.ShouldNotBeNull();
        memberships.Leagues.ShouldHaveSingleItem().LeagueId.ShouldBe(leagueId);

        var imported = (await session.Events.FetchStreamAsync(leagueId)).Select(e => e.Data).OfType<LeagueImported>().Single();
        var snapshot = await session.LoadAsync<SleeperSnapshot>(imported.SnapshotId);
        snapshot.ShouldNotBeNull();
        snapshot.SleeperLeagueId.ShouldBe(sleeperLeagueId);
        snapshot.League.ShouldContain("\"Holland Hogs\"");
        snapshot.Rosters.ShouldContain("co_owners");
    }

    [Fact]
    public async Task The_Sleeper_responses_stay_inside_the_league()
    {
        var leagueId = Guid.NewGuid();
        (await Import(NewSubject(), leagueId, Api.Sleeper.CopyOfHollandHogs())).StatusCode.ShouldBe(HttpStatusCode.Created);

        await using var inTheLeague = Store.QuerySession(leagueId.ToString());
        await using var elsewhere = Store.QuerySession(Guid.NewGuid().ToString());

        (await inTheLeague.Query<SleeperSnapshot>().CountAsync()).ShouldBe(1);
        (await elsewhere.Query<SleeperSnapshot>().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Someone_who_owns_no_team_in_the_league_is_forbidden_and_nothing_is_written()
    {
        var subject = NewSubject();
        var leagueId = Guid.NewGuid();
        var sleeperLeagueId = Api.Sleeper.CopyOfHollandHogs();
        Api.Sleeper.ServeUser("dana", "100000000000000009");

        var response = await Import(subject, leagueId, sleeperLeagueId, username: "dana");

        (await ProblemFrom(response, HttpStatusCode.Forbidden)).Detail
            .ShouldBe("dana does not own a team in Holland Hogs on Sleeper, so cannot import it.");
        await NothingWritten(subject, leagueId, sleeperLeagueId);
    }

    [Fact]
    public async Task A_Sleeper_league_that_backs_another_league_is_a_conflict_and_nothing_is_written()
    {
        var sleeperLeagueId = Api.Sleeper.CopyOfHollandHogs();
        (await Import(NewSubject(), Guid.NewGuid(), sleeperLeagueId)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var secondImporter = NewSubject();
        var secondLeague = Guid.NewGuid();

        var response = await Import(secondImporter, secondLeague, sleeperLeagueId);

        (await ProblemFrom(response, HttpStatusCode.Conflict)).Detail
            .ShouldBe("Holland Hogs is already kept in BallBank by another treasurer. Ask them for an invite.");
        await NothingWritten(secondImporter, secondLeague, sleeperLeagueId: null);
    }

    [Fact]
    public async Task When_Sleeper_fails_part_way_the_caller_is_told_to_try_again_and_nothing_is_written()
    {
        var subject = NewSubject();
        var leagueId = Guid.NewGuid();
        var sleeperLeagueId = Api.Sleeper.CopyOfHollandHogs();
        Api.Sleeper.Unreachable($"/league/{sleeperLeagueId}/rosters");

        var response = await Import(subject, leagueId, sleeperLeagueId);

        (await ProblemFrom(response, HttpStatusCode.BadGateway)).Detail
            .ShouldBe("Sleeper is not answering right now. Try again in a minute.");
        await NothingWritten(subject, leagueId, sleeperLeagueId);
    }

    [Fact]
    public async Task Two_imports_of_one_Sleeper_league_at_once_leave_one_league()
    {
        var sleeperLeagueId = Api.Sleeper.CopyOfHollandHogs();
        var first = (Subject: NewSubject(), LeagueId: Guid.NewGuid());
        var second = (Subject: NewSubject(), LeagueId: Guid.NewGuid());

        var responses = await Task.WhenAll(
            Import(first.Subject, first.LeagueId, sleeperLeagueId),
            Import(second.Subject, second.LeagueId, sleeperLeagueId));

        responses.Select(r => r.StatusCode).ShouldBe([HttpStatusCode.Created, HttpStatusCode.Conflict], ignoreOrder: true);
        var loser = responses[0].StatusCode == HttpStatusCode.Created ? second : first;
        await NothingWritten(loser.Subject, loser.LeagueId, sleeperLeagueId: null);
    }

    [Fact]
    public async Task Retrying_an_import_returns_the_league_without_importing_it_again()
    {
        var subject = NewSubject();
        var leagueId = Guid.NewGuid();
        var sleeperLeagueId = Api.Sleeper.CopyOfHollandHogs();
        var first = await (await Import(subject, leagueId, sleeperLeagueId)).Content.ReadFromJsonAsync<ImportedLeague>();

        var retry = await Import(subject, leagueId, sleeperLeagueId);

        retry.StatusCode.ShouldBe(HttpStatusCode.OK);
        var again = await retry.Content.ReadFromJsonAsync<ImportedLeague>();
        again.ShouldNotBeNull();
        again.MemberId.ShouldBe(first!.MemberId);
        await using var session = Store.QuerySession(leagueId.ToString());
        (await session.Events.FetchStreamAsync(leagueId)).OfType<IEvent>().Count(e => e.Data is LeagueImported).ShouldBe(1);
        (await session.Query<SleeperSnapshot>().CountAsync()).ShouldBe(1);
        (await session.LoadAsync<UserMemberships>(subject))!.Leagues.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Someone_else_cannot_import_into_a_league_that_exists()
    {
        var leagueId = Guid.NewGuid();
        var sleeperLeagueId = Api.Sleeper.CopyOfHollandHogs();
        (await Import(NewSubject(), leagueId, sleeperLeagueId)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await Import(NewSubject(), leagueId, sleeperLeagueId);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Without_a_token_the_caller_is_unauthorized()
    {
        var response = await Api.CreateClient().PostAsJsonAsync(
            $"/leagues/{Guid.NewGuid()}/import",
            new ImportLeagueRequest(FakeSleeper.HollandHogsLeagueId, "jacob", "Jacob"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private Task<HttpResponseMessage> Import(string subject, Guid leagueId, string sleeperLeagueId, string username = "jacob") =>
        Api.CreateClientFor(subject).PostAsJsonAsync(
            $"/leagues/{leagueId}/import",
            new ImportLeagueRequest(sleeperLeagueId, username, "Jacob"));

    /// <summary>No league stream or snapshot, no membership for the importer and, when given, no index entry for the Sleeper league.</summary>
    private async Task NothingWritten(string subject, Guid leagueId, string? sleeperLeagueId)
    {
        await using var session = Store.QuerySession(leagueId.ToString());

        (await session.Events.FetchStreamStateAsync(leagueId)).ShouldBeNull();
        (await session.Query<SleeperSnapshot>().CountAsync()).ShouldBe(0);
        (await session.LoadAsync<UserMemberships>(subject)).ShouldBeNull();
        if (sleeperLeagueId is not null)
        {
            (await session.LoadAsync<SleeperLeagueIndex>(sleeperLeagueId)).ShouldBeNull();
        }
    }

    private static async Task<ProblemDetails> ProblemFrom(HttpResponseMessage response, HttpStatusCode expected)
    {
        response.StatusCode.ShouldBe(expected);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.ShouldNotBeNull();
        return problem;
    }

    private static string NewSubject() => $"test|{Guid.NewGuid():N}";
}

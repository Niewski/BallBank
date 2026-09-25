using System.Net;
using System.Net.Http.Json;
using BallBank.Api.Features.Membership;
using BallBank.Integration.Tests.Sleeper;
using Microsoft.AspNetCore.Mvc;

namespace BallBank.Integration.Tests.Http;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class PickingASleeperLeagueTests(PostgresFixture postgres)
{
    private BallBankApi Api => postgres.Api;

    [Fact]
    public async Task A_person_sees_their_Sleeper_leagues_for_the_current_season()
    {
        var leagues = await SignedIn().GetFromJsonAsync<SleeperLeagueChoice[]>("/sleeper/users/jacob/leagues");

        leagues.ShouldNotBeNull();
        leagues.ShouldHaveSingleItem().ShouldBe(
            new SleeperLeagueChoice(FakeSleeper.HollandHogsLeagueId, "Holland Hogs", "2026", Teams: 4));
    }

    [Fact]
    public async Task Without_a_token_the_caller_is_unauthorized()
    {
        var response = await Api.CreateClient().GetAsync("/sleeper/users/jacob/leagues");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_username_Sleeper_does_not_know_is_not_found_with_a_message()
    {
        var username = NewUsername();
        Api.Sleeper.ServeJson($"/user/{username}", "null");

        var problem = await ProblemFrom(await SignedIn().GetAsync($"/sleeper/users/{username}/leagues"), HttpStatusCode.NotFound);

        problem.Detail.ShouldBe($"Sleeper has no user called \"{username}\". Check the spelling of your Sleeper username.");
    }

    [Fact]
    public async Task A_username_Sleeper_answers_404_for_is_not_found()
    {
        var response = await SignedIn().GetAsync($"/sleeper/users/{NewUsername()}/leagues");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task When_Sleeper_is_unreachable_the_caller_is_told_to_try_again()
    {
        var username = NewUsername();
        Api.Sleeper.Unreachable($"/user/{username}");

        var problem = await ProblemFrom(await SignedIn().GetAsync($"/sleeper/users/{username}/leagues"), HttpStatusCode.BadGateway);

        problem.Detail.ShouldBe("Sleeper is not answering right now. Try again in a minute.");
    }

    [Fact]
    public async Task When_Sleeper_is_slow_the_caller_is_told_to_try_again()
    {
        var username = NewUsername();
        Api.Sleeper.Hangs($"/user/{username}");

        var problem = await ProblemFrom(await SignedIn().GetAsync($"/sleeper/users/{username}/leagues"), HttpStatusCode.BadGateway);

        problem.Detail.ShouldBe("Sleeper is not answering right now. Try again in a minute.");
    }

    [Fact]
    public async Task When_Sleeper_answers_with_an_error_the_caller_is_told_to_try_again()
    {
        var username = NewUsername();
        Api.Sleeper.Fails($"/user/{username}", HttpStatusCode.ServiceUnavailable);

        var problem = await ProblemFrom(await SignedIn().GetAsync($"/sleeper/users/{username}/leagues"), HttpStatusCode.BadGateway);

        problem.Detail.ShouldBe("Sleeper is not answering right now. Try again in a minute.");
    }

    private static async Task<ProblemDetails> ProblemFrom(HttpResponseMessage response, HttpStatusCode expected)
    {
        response.StatusCode.ShouldBe(expected);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.ShouldNotBeNull();
        return problem;
    }

    private HttpClient SignedIn() => Api.CreateClientFor($"test|{Guid.NewGuid():N}");

    private static string NewUsername() => $"user{Guid.NewGuid():N}"[..20];
}

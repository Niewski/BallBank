using System.Net;
using System.Net.Http.Json;
using BallBank.Api.Features.Membership;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace BallBank.Integration.Tests.Http;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class MyLeaguesTests(PostgresFixture postgres)
{
    private BallBankApi Api => postgres.Api;

    [Fact]
    public async Task Without_a_token_the_caller_is_unauthorized()
    {
        var response = await Api.CreateClient().GetAsync("/me/leagues");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_signed_by_someone_else_is_unauthorized()
    {
        var forged = Api.TokenFor(NewSubject(), signingKey: BallBankApi.NewSigningKey());

        var response = await GetMyLeagues(forged);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_from_another_issuer_is_unauthorized()
    {
        var response = await GetMyLeagues(Api.TokenFor(NewSubject(), issuer: "https://elsewhere.invalid/"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_for_another_audience_is_unauthorized()
    {
        var response = await GetMyLeagues(Api.TokenFor(NewSubject(), audience: "https://another-api.invalid"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_expired_token_is_unauthorized()
    {
        var response = await GetMyLeagues(Api.TokenFor(NewSubject(), expires: DateTime.UtcNow.AddHours(-1)));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_without_a_subject_is_unauthorized()
    {
        var response = await GetMyLeagues(Api.TokenFor(subject: null));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_person_who_belongs_to_no_league_sees_an_empty_list()
    {
        var leagues = await Api.CreateClientFor(NewSubject()).GetFromJsonAsync<MyLeague[]>("/me/leagues");

        leagues.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_member_sees_each_league_they_belong_to()
    {
        var subject = NewSubject();
        var hogs = new LeagueMembership(Guid.NewGuid(), "Holland Hogs", "2026", Guid.NewGuid(), [Roles.Treasurer]);
        var office = new LeagueMembership(Guid.NewGuid(), "Office League", "2026", Guid.NewGuid(), []);
        await StoreMemberships(new UserMemberships { Id = subject, Leagues = [hogs, office] });
        await StoreMemberships(new UserMemberships { Id = NewSubject(), Leagues = [Other()] });

        var leagues = await Api.CreateClientFor(subject).GetFromJsonAsync<MyLeague[]>("/me/leagues");

        leagues.ShouldNotBeNull();
        leagues.Select(l => (l.LeagueId, l.Name, l.Season, l.MemberId, string.Join(",", l.Roles)))
            .ShouldBe(
            [
                (hogs.LeagueId, "Holland Hogs", "2026", hogs.MemberId, "Treasurer"),
                (office.LeagueId, "Office League", "2026", office.MemberId, ""),
            ],
            ignoreOrder: true);
    }

    [Fact]
    public async Task Memberships_are_shared_by_every_league_rather_than_held_by_one()
    {
        var subject = NewSubject();
        await StoreMemberships(new UserMemberships { Id = subject, Leagues = [Other()] });

        var store = Api.Services.GetRequiredService<IDocumentStore>();
        await using var inALeague = store.QuerySession($"league-{Guid.NewGuid():N}");

        var memberships = await inALeague.LoadAsync<UserMemberships>(subject);

        memberships.ShouldNotBeNull();
        memberships.Leagues.ShouldHaveSingleItem();
    }

    private Task<HttpResponseMessage> GetMyLeagues(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/me/leagues");
        request.Headers.Authorization = new("Bearer", token);
        return Api.CreateClient().SendAsync(request);
    }

    private async Task StoreMemberships(UserMemberships memberships)
    {
        await using var session = Api.Services.GetRequiredService<IDocumentStore>().LightweightSession();
        session.Store(memberships);
        await session.SaveChangesAsync();
    }

    private static LeagueMembership Other() =>
        new(Guid.NewGuid(), "Someone Else's League", "2026", Guid.NewGuid(), []);

    private static string NewSubject() => $"test|{Guid.NewGuid():N}";
}

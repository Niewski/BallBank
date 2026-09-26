using System.Net;
using System.Net.Http.Json;
using BallBank.Api;
using BallBank.Api.Features.Membership;
using BallBank.Api.Features.Treasury;
using BallBank.Domain.Membership;
using BallBank.Domain.Treasury;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace BallBank.Integration.Tests.Http;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class OpeningASeasonTests(PostgresFixture postgres)
{
    private static readonly DateOnly DueDate = new(2026, 10, 1);

    private BallBankApi Api => postgres.Api;
    private IDocumentStore Store => Api.Services.GetRequiredService<IDocumentStore>();

    [Fact]
    public async Task Opening_a_season_assesses_every_current_member()
    {
        var hogs = await HollandHogs.Import(this);

        var response = await Open(hogs.Jacob, hogs);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var summary = (await response.Content.ReadFromJsonAsync<SeasonSummary>()).ShouldNotBeNull();
        summary.Label.ShouldBe("2026");
        summary.Status.ShouldBe(SeasonStatus.Open);
        summary.DuesAmount.ShouldBe(50m);
        summary.DueDate.ShouldBe(DueDate);

        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        var statements = await session.Query<MemberStatement>().ToListAsync();
        statements.Count.ShouldBe(4);
        statements.ShouldAllBe(s => s.Lines.Single().Amount == 50m && s.Lines.Single().DueDate == DueDate);
    }

    [Fact]
    public async Task Opening_the_same_season_twice_assesses_once()
    {
        var hogs = await HollandHogs.Import(this);

        (await Open(hogs.Jacob, hogs)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var again = await Open(hogs.Jacob, hogs);

        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await again.Content.ReadFromJsonAsync<SeasonSummary>()).ShouldNotBeNull().DuesAmount.ShouldBe(50m);

        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        var statements = await session.Query<MemberStatement>().ToListAsync();
        statements.Count.ShouldBe(4);
        statements.ShouldAllBe(s => s.Lines.Count == 1);
    }

    [Fact]
    public async Task The_history_records_who_opened_the_season()
    {
        var hogs = await HollandHogs.Import(this);

        await Open(hogs.Jacob, hogs);

        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        var seasonId = SeasonIds.SeasonId(hogs.LeagueId, "2026");
        var events = await session.Events.FetchStreamAsync(seasonId);
        var stored = events.Single();

        ((SeasonOpened)stored.Data).OpenedBy.ShouldBe(hogs.Jacobs);
        stored.GetHeader(EventHeaders.Subject)?.ToString().ShouldBe(hogs.Jacob);
    }

    [Fact]
    public async Task A_new_season_gives_every_member_an_account_named_from_the_season_and_member()
    {
        var hogs = await HollandHogs.Import(this);

        await Open(hogs.Jacob, hogs);

        var seasonId = SeasonIds.SeasonId(hogs.LeagueId, "2026");
        var accountId = SeasonIds.AccountId(seasonId, hogs.Jacobs);

        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        (await session.Events.AggregateStreamAsync<MemberAccount>(accountId)).ShouldNotBeNull().Balance.ShouldBe(50m);
    }

    [Fact]
    public async Task A_member_who_is_not_a_treasurer_is_forbidden_to_open_a_season()
    {
        var hogs = await HollandHogs.Import(this);

        var response = await Open(hogs.Sam, hogs);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Someone_outside_the_league_is_forbidden_to_open_a_season()
    {
        var hogs = await HollandHogs.Import(this);

        var response = await Open($"test|{Guid.NewGuid():N}", hogs);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Season_dues_must_be_a_positive_amount()
    {
        var hogs = await HollandHogs.Import(this);

        var response = await Open(hogs.Jacob, hogs, amount: 0m);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>()).ShouldNotBeNull()
            .Detail.ShouldBe("Season dues must be a positive amount.");
    }

    [Fact]
    public async Task A_season_needs_a_due_date()
    {
        var hogs = await HollandHogs.Import(this);

        var response = await Send(Api.CreateClientFor(hogs.Jacob), hogs.LeagueId, new OpenSeasonRequest("2026", 50m, DueDate: null));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>()).ShouldNotBeNull()
            .Detail.ShouldBe("A season needs a due date.");
    }

    [Fact]
    public async Task A_league_can_list_its_open_season()
    {
        var hogs = await HollandHogs.Import(this);
        await Open(hogs.Jacob, hogs);

        var seasons = await Api.CreateClientFor(hogs.Sam).GetFromJsonAsync<SeasonSummary[]>($"/leagues/{hogs.LeagueId}/seasons");

        seasons.ShouldNotBeNull().ShouldHaveSingleItem().Label.ShouldBe("2026");
    }

    [Fact]
    public async Task Without_a_token_the_caller_is_unauthorized()
    {
        var response = await Api.CreateClient().PostAsJsonAsync(
            $"/leagues/{Guid.NewGuid()}/seasons",
            new OpenSeasonRequest("2026", 50m, DueDate));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Opening_a_season_without_an_idempotency_key_is_a_precondition_failure()
    {
        var hogs = await HollandHogs.Import(this);

        var response = await Api.CreateClientFor(hogs.Jacob).PostAsJsonAsync(
            $"/leagues/{hogs.LeagueId}/seasons",
            new OpenSeasonRequest("2026", 50m, DueDate));

        response.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>()).ShouldNotBeNull()
            .Title.ShouldBe("Idempotency-Key required");
    }

    [Fact]
    public async Task A_retried_request_answers_as_the_first_try_did_and_records_nothing_new()
    {
        var hogs = await HollandHogs.Import(this);
        var key = Guid.NewGuid().ToString();

        var first = await Open(hogs.Jacob, hogs, idempotencyKey: key);
        var retry = await Open(hogs.Jacob, hogs, idempotencyKey: key);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        retry.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await retry.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());

        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        var state = await session.Events.FetchStreamStateAsync(SeasonIds.SeasonId(hogs.LeagueId, "2026"));
        state.ShouldNotBeNull().Version.ShouldBe(1);
    }

    [Fact]
    public async Task Two_tries_of_one_request_in_flight_at_once_both_answer_as_the_one_that_committed()
    {
        var hogs = await HollandHogs.Import(this);
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(Open(hogs.Jacob, hogs, idempotencyKey: key), Open(hogs.Jacob, hogs, idempotencyKey: key));

        responses.ShouldAllBe(response => response.StatusCode == HttpStatusCode.Created);
        (await responses[1].Content.ReadAsStringAsync()).ShouldBe(await responses[0].Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_idempotency_key_reused_for_a_different_request_is_unprocessable()
    {
        var hogs = await HollandHogs.Import(this);
        var key = Guid.NewGuid().ToString();
        await Open(hogs.Jacob, hogs, idempotencyKey: key);

        var response = await Open(hogs.Jacob, hogs, amount: 60m, idempotencyKey: key);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>()).ShouldNotBeNull()
            .Title.ShouldBe("Idempotency-Key reused");
    }

    [Fact]
    public async Task A_refused_request_records_no_answer_so_a_corrected_retry_goes_through()
    {
        var hogs = await HollandHogs.Import(this);
        var key = Guid.NewGuid().ToString();

        (await Open(hogs.Jacob, hogs, amount: 0m, idempotencyKey: key)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var corrected = await Open(hogs.Jacob, hogs, idempotencyKey: key);

        corrected.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Two_treasurers_using_the_same_idempotency_key_do_not_collide()
    {
        var hogs = await HollandHogs.Import(this);
        (await Api.CreateClientFor(hogs.Jacob).PostAsJsonAsync(
            $"/leagues/{hogs.LeagueId}/treasurers", new AppointTreasurerRequest(hogs.Sams))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var key = Guid.NewGuid().ToString();
        await Open(hogs.Jacob, hogs, label: "2026", idempotencyKey: key);

        var response = await Open(hogs.Sam, hogs, label: "2027", idempotencyKey: key);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<SeasonSummary>()).ShouldNotBeNull().Label.ShouldBe("2027");
    }

    [Fact]
    public async Task The_same_idempotency_key_in_two_leagues_does_not_collide()
    {
        var hogs = await HollandHogs.Import(this);
        var otherHogs = await HollandHogs.Import(this, hogs.Jacob);
        var key = Guid.NewGuid().ToString();
        await Open(hogs.Jacob, hogs, idempotencyKey: key);

        var response = await Open(hogs.Jacob, otherHogs, idempotencyKey: key);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        await using var session = Store.QuerySession(otherHogs.LeagueId.ToString());
        (await session.Query<MemberStatement>().CountAsync()).ShouldBe(4);
    }

    private Task<HttpResponseMessage> Open(
        string subject,
        HollandHogs hogs,
        string label = "2026",
        decimal amount = 50m,
        DateOnly? dueDate = null,
        string? idempotencyKey = null) =>
        Send(Api.CreateClientFor(subject), hogs.LeagueId, new OpenSeasonRequest(label, amount, dueDate ?? DueDate), idempotencyKey);

    private static Task<HttpResponseMessage> Send(HttpClient client, Guid leagueId, OpenSeasonRequest request, string? idempotencyKey = null)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, $"/leagues/{leagueId}/seasons")
        {
            Content = JsonContent.Create(request),
        };
        message.Headers.Add(Idempotency.Header, idempotencyKey ?? Guid.NewGuid().ToString());
        return client.SendAsync(message);
    }

    /// <summary>Holland Hogs as imported by Jacob, who keeps its books, with Sam holding Sam's Slammers.</summary>
    private sealed record HollandHogs(Guid LeagueId, string Jacob, Guid Jacobs, string Sam, Guid Sams)
    {
        public static async Task<HollandHogs> Import(OpeningASeasonTests tests, string? jacob = null)
        {
            var leagueId = Guid.NewGuid();
            jacob ??= NewSubject();
            var response = await tests.Api.CreateClientFor(jacob).PostAsJsonAsync(
                $"/leagues/{leagueId}/import",
                new ImportLeagueRequest(tests.Api.Sleeper.CopyOfHollandHogs(), "jacob", "Jacob"));
            response.StatusCode.ShouldBe(HttpStatusCode.Created);

            var sam = NewSubject();
            var league = await tests.ClaimAsync(leagueId, rosterId: 2, sam, "Sam");

            var jacobs = league.Members.Single(m => m.SleeperRosterId == 1).MemberId;
            var sams = league.Members.Single(m => m.SleeperRosterId == 2).MemberId;
            return new HollandHogs(leagueId, jacob, jacobs, sam, sams);
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

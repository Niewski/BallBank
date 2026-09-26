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
public class AssessingTests(PostgresFixture postgres)
{
    private static readonly DateOnly SeasonDueDate = new(2026, 10, 1);
    private static readonly DateOnly DueDate = new(2026, 11, 1);

    private BallBankApi Api => postgres.Api;
    private IDocumentStore Store => Api.Services.GetRequiredService<IDocumentStore>();

    [Fact]
    public async Task Assessing_everyone_adds_one_assessment_to_every_account()
    {
        var hogs = await HollandHogs.OpenSeason(this);

        var response = await Assess(hogs.Jacob, hogs, LateFee());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var touched = (await response.Content.ReadFromJsonAsync<AssessedAccount[]>()).ShouldNotBeNull();
        touched.Length.ShouldBe(4);
        touched.ShouldAllBe(a => !a.Opened);
        (await Ledger(hogs)).Select(a => a.Balance).ShouldBe([60m, 60m, 60m, 60m]);
    }

    [Fact]
    public async Task Each_assessment_shows_its_memo_amount_and_due_date_on_the_statement()
    {
        var hogs = await HollandHogs.OpenSeason(this);

        await Assess(hogs.Jacob, hogs, LateFee());

        var statement = await Api.CreateClientFor(hogs.Sam).GetFromJsonAsync<AccountStatement>(
            $"/leagues/{hogs.LeagueId}/accounts/{hogs.AccountOf(hogs.Sams)}");
        statement.ShouldNotBeNull().Lines.Select(l => (l.Memo, l.Amount, l.DueDate, l.ByName)).ShouldBe(
        [
            ("Season dues", 50m, SeasonDueDate, "Jacob"),
            ("Late fee", 10m, DueDate, "Jacob"),
        ]);
    }

    [Fact]
    public async Task Assessing_chosen_members_touches_only_their_accounts()
    {
        var hogs = await HollandHogs.OpenSeason(this);

        var response = await Assess(hogs.Jacob, hogs, SidePot([hogs.Jacobs, hogs.Sams]));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<AssessedAccount[]>()).ShouldNotBeNull()
            .Select(a => a.MemberId).ShouldBe([hogs.Jacobs, hogs.Sams], ignoreOrder: true);
        (await Ledger(hogs)).Select(a => (a.MemberId, a.Balance)).ShouldBe(
        [
            (hogs.Jacobs, 75m),
            (hogs.Priyas, 50m),
            (hogs.Sams, 75m),
            (hogs.Team4, 50m),
        ]);
    }

    [Fact]
    public async Task Repeating_an_assessment_adds_nothing()
    {
        var hogs = await HollandHogs.OpenSeason(this);
        var lateFee = LateFee();
        await Assess(hogs.Jacob, hogs, lateFee);

        var again = await Assess(hogs.Jacob, hogs, lateFee);

        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await again.Content.ReadFromJsonAsync<AssessedAccount[]>()).ShouldNotBeNull().ShouldBeEmpty();
        (await Ledger(hogs)).ShouldAllBe(a => a.Balance == 60m && a.Version == 3);
    }

    [Fact]
    public async Task A_member_added_since_the_season_opened_gets_an_account_with_their_first_assessment()
    {
        var hogs = await HollandHogs.OpenSeason(this);
        var danas = await hogs.ImportAddsDana(this);

        var response = await Assess(hogs.Jacob, hogs, new AssessmentRequest(Guid.NewGuid(), 50m, SeasonDueDate, "Season dues", [danas]));

        (await response.Content.ReadFromJsonAsync<AssessedAccount[]>()).ShouldNotBeNull()
            .ShouldHaveSingleItem().ShouldBe(new AssessedAccount(hogs.AccountOf(danas), danas, Opened: true));
        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        var events = await session.Events.FetchStreamAsync(hogs.AccountOf(danas));
        events.Select(e => e.Data.GetType()).ShouldBe([typeof(AccountOpened), typeof(DuesAssessed)]);
        events.Select(e => e.GetHeader(EventHeaders.Subject)?.ToString()).ShouldAllBe(subject => subject == hogs.Jacob);
        (await Ledger(hogs)).Single(a => a.MemberId == danas).Balance.ShouldBe(50m);
    }

    [Fact]
    public async Task Assessing_everyone_includes_a_member_added_since_the_season_opened()
    {
        var hogs = await HollandHogs.OpenSeason(this);
        var danas = await hogs.ImportAddsDana(this);

        var touched = await (await Assess(hogs.Jacob, hogs, LateFee())).Content.ReadFromJsonAsync<AssessedAccount[]>();

        touched.ShouldNotBeNull().Length.ShouldBe(5);
        touched.Single(a => a.MemberId == danas).Opened.ShouldBeTrue();
        (await Ledger(hogs)).Single(a => a.MemberId == danas).Balance.ShouldBe(10m);
    }

    [Fact]
    public async Task An_amount_beyond_whole_cents_is_refused_and_records_nothing()
    {
        var hogs = await HollandHogs.OpenSeason(this);

        var response = await Assess(hogs.Jacob, hogs, LateFee() with { Amount = 10.005m });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>()).ShouldNotBeNull()
            .Detail.ShouldBe("An assessment must be in whole cents.");
        (await Ledger(hogs)).ShouldAllBe(a => a.Balance == 50m);
    }

    [Fact]
    public async Task A_refusal_for_one_member_records_nothing_for_the_others()
    {
        var hogs = await HollandHogs.OpenSeason(this);
        var danas = await hogs.ImportAddsDana(this);

        var response = await Assess(hogs.Jacob, hogs, LateFee() with { DueDate = null });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Ledger(hogs)).ShouldAllBe(a => a.Balance == 50m);
        (await Ledger(hogs)).ShouldNotContain(a => a.MemberId == danas);
    }

    [Fact]
    public async Task Assessing_someone_who_is_not_a_member_is_refused()
    {
        var hogs = await HollandHogs.OpenSeason(this);

        var response = await Assess(hogs.Jacob, hogs, SidePot([hogs.Sams, Guid.NewGuid()]));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Ledger(hogs)).ShouldAllBe(a => a.Balance == 50m);
    }

    [Fact]
    public async Task Assessing_an_empty_list_of_members_is_refused()
    {
        var hogs = await HollandHogs.OpenSeason(this);

        var response = await Assess(hogs.Jacob, hogs, SidePot([]));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Ledger(hogs)).ShouldAllBe(a => a.Balance == 50m);
    }

    [Fact]
    public async Task A_member_who_is_not_a_treasurer_is_forbidden_to_assess()
    {
        var hogs = await HollandHogs.OpenSeason(this);

        var response = await Assess(hogs.Sam, hogs, LateFee());

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Ledger(hogs)).ShouldAllBe(a => a.Balance == 50m);
    }

    [Fact]
    public async Task Assessing_without_an_idempotency_key_is_a_precondition_failure()
    {
        var hogs = await HollandHogs.OpenSeason(this);

        var response = await Api.CreateClientFor(hogs.Jacob).PostAsJsonAsync(
            $"/leagues/{hogs.LeagueId}/seasons/2026/assessments", LateFee());

        response.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
    }

    [Fact]
    public async Task Assessing_in_a_season_that_is_not_open_is_not_found()
    {
        var hogs = await HollandHogs.OpenSeason(this);

        var response = await Assess(hogs.Jacob, hogs, LateFee(), season: "1999");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_retried_request_answers_as_the_first_try_did()
    {
        var hogs = await HollandHogs.OpenSeason(this);
        var key = Guid.NewGuid().ToString();
        var lateFee = LateFee();

        var first = await Assess(hogs.Jacob, hogs, lateFee, idempotencyKey: key);
        var retry = await Assess(hogs.Jacob, hogs, lateFee, idempotencyKey: key);

        retry.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await retry.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());
        (await Ledger(hogs)).ShouldAllBe(a => a.Balance == 60m);
    }

    [Fact]
    public async Task Two_treasurers_sending_one_assessment_at_once_bill_each_member_once()
    {
        var hogs = await HollandHogs.OpenSeason(this);
        var lateFee = LateFee();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Assess(hogs.Jacob, hogs, lateFee)));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.Conflict);
        responses.ShouldContain(r => r.StatusCode == HttpStatusCode.OK);
        (await Ledger(hogs)).ShouldAllBe(a => a.Balance == 60m);
    }

    private static AssessmentRequest LateFee() => new(Guid.NewGuid(), 10m, DueDate, "Late fee", MemberIds: null);

    private static AssessmentRequest SidePot(Guid[] memberIds) => new(Guid.NewGuid(), 25m, DueDate, "Side pot", memberIds);

    private async Task<LedgerEntry[]> Ledger(HollandHogs hogs) =>
        (await Api.CreateClientFor(hogs.Jacob).GetFromJsonAsync<LedgerEntry[]>($"/leagues/{hogs.LeagueId}/seasons/2026/ledger"))
        .ShouldNotBeNull()
        .OrderBy(a => a.MemberId == hogs.Jacobs ? 0 : a.MemberId == hogs.Priyas ? 1 : a.MemberId == hogs.Sams ? 2 : a.MemberId == hogs.Team4 ? 3 : 4)
        .ToArray();

    private Task<HttpResponseMessage> Assess(
        string subject,
        HollandHogs hogs,
        AssessmentRequest request,
        string season = "2026",
        string? idempotencyKey = null) =>
        Send(Api.CreateClientFor(subject), $"/leagues/{hogs.LeagueId}/seasons/{season}/assessments", request, idempotencyKey);

    private static Task<HttpResponseMessage> Send(HttpClient client, string path, object body, string? idempotencyKey = null)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        message.Headers.Add(Idempotency.Header, idempotencyKey ?? Guid.NewGuid().ToString());
        return client.SendAsync(message);
    }

    /// <summary>
    /// Holland Hogs as imported by Jacob, who keeps its books, with Sam holding Sam's Slammers and the
    /// 2026 season open at $50: four members, Priya's and an ownerless Team 4 among them.
    /// </summary>
    private sealed record HollandHogs(Guid LeagueId, string SleeperLeagueId, string Jacob, Guid Jacobs, string Sam, Guid Sams, Guid Priyas, Guid Team4)
    {
        public Guid AccountOf(Guid memberId) => SeasonIds.AccountId(SeasonIds.SeasonId(LeagueId, "2026"), memberId);

        public static async Task<HollandHogs> OpenSeason(AssessingTests tests)
        {
            var leagueId = Guid.NewGuid();
            var sleeperLeagueId = tests.Api.Sleeper.CopyOfHollandHogs();
            var jacob = NewSubject();
            var jacobsClient = tests.Api.CreateClientFor(jacob);
            (await jacobsClient.PostAsJsonAsync(
                $"/leagues/{leagueId}/import",
                new ImportLeagueRequest(sleeperLeagueId, "jacob", "Jacob"))).StatusCode.ShouldBe(HttpStatusCode.Created);

            var sam = NewSubject();
            var league = await tests.ClaimAsync(leagueId, rosterId: 2, sam, "Sam");

            (await Send(jacobsClient, $"/leagues/{leagueId}/seasons", new OpenSeasonRequest("2026", 50m, SeasonDueDate)))
                .StatusCode.ShouldBe(HttpStatusCode.Created);

            Guid MemberOn(int rosterId) => league.Members.Single(m => m.SleeperRosterId == rosterId).MemberId;
            return new HollandHogs(leagueId, sleeperLeagueId, jacob, MemberOn(1), sam, MemberOn(2), MemberOn(3), MemberOn(4));
        }

        /// <summary>A roster for Dana appears on Sleeper and Jacob imports the league again; returns Dana's member id.</summary>
        public async Task<Guid> ImportAddsDana(AssessingTests tests)
        {
            tests.Api.Sleeper.TeamJoins(SleeperLeagueId, rosterId: 5, ownerUserId: "100000000000000099", ownerName: "Dana", teamName: "Dana's Dynasty");
            (await tests.Api.CreateClientFor(Jacob).PostAsJsonAsync($"/leagues/{LeagueId}/import", new { }))
                .IsSuccessStatusCode.ShouldBeTrue();

            await using var session = tests.Store.QuerySession(LeagueId.ToString());
            var league = (await session.Events.AggregateStreamAsync<League>(LeagueId)).ShouldNotBeNull();
            return league.Members.Single(m => m.SleeperRosterId == 5).MemberId;
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

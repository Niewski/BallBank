using System.Net;
using System.Net.Http.Json;
using BallBank.Api.Features.Membership;
using BallBank.Api.Features.Treasury;
using BallBank.Domain.Membership;
using BallBank.Domain.Treasury;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace BallBank.Integration.Tests.Http;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class LedgerTests(PostgresFixture postgres)
{
    private static readonly DateOnly DueDate = new(2026, 10, 1);

    private BallBankApi Api => postgres.Api;
    private IDocumentStore Store => Api.Services.GetRequiredService<IDocumentStore>();

    [Fact]
    public async Task The_ledger_lists_every_account_with_its_balance_pending_count_and_version()
    {
        var hogs = await HollandHogs.OpenSeason(this);
        var paid = Guid.NewGuid();
        await Record(hogs, hogs.Jacobs,
            new PaymentAttested(paid, 20m, PaymentRail.Venmo, "VN-1234", hogs.Jacobs, DateTimeOffset.UtcNow),
            new PaymentConfirmed(paid, hogs.Jacobs, DateTimeOffset.UtcNow));
        await Record(hogs, hogs.Sams,
            new PaymentAttested(Guid.NewGuid(), 50m, PaymentRail.Cash, null, hogs.Sams, DateTimeOffset.UtcNow));

        var ledger = await Api.CreateClientFor(hogs.Sam).GetFromJsonAsync<LedgerEntry[]>($"/leagues/{hogs.LeagueId}/seasons/2026/ledger");

        ledger.ShouldNotBeNull();
        ledger.Select(a => (a.TeamName, a.DisplayName, a.Balance, a.PendingAttestations, a.Version)).ShouldBe(
        [
            ("Hog Wild", "Jacob", 30m, 0, 4),
            ("Priya", "Priya", 50m, 0, 2),
            ("Sam's Slammers", "Sam", 50m, 1, 3),
            ("Team 4", null, 50m, 0, 2),
        ]);
        ledger.Single(a => a.MemberId == hogs.Sams).AccountId.ShouldBe(hogs.AccountOf(hogs.Sams));
    }

    [Fact]
    public async Task The_ledger_of_a_league_the_caller_is_not_in_is_forbidden()
    {
        var hogs = await HollandHogs.OpenSeason(this);
        var someoneElses = await HollandHogs.OpenSeason(this);

        var response = await Api.CreateClientFor(hogs.Sam).GetAsync($"/leagues/{someoneElses.LeagueId}/seasons/2026/ledger");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_ledger_of_an_unknown_season_is_not_found()
    {
        var hogs = await HollandHogs.OpenSeason(this);

        var response = await Api.CreateClientFor(hogs.Sam).GetAsync($"/leagues/{hogs.LeagueId}/seasons/1999/ledger");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_member_reads_their_own_statement()
    {
        var hogs = await HollandHogs.OpenSeason(this);
        var rejected = Guid.NewGuid();
        await Record(hogs, hogs.Sams,
            new PaymentAttested(rejected, 50m, PaymentRail.Zelle, "ZL-0000", hogs.Sams, DateTimeOffset.UtcNow),
            new PaymentRejected(rejected, hogs.Jacobs, "No Zelle with that reference arrived", DateTimeOffset.UtcNow),
            new PaymentAttested(Guid.NewGuid(), 50m, PaymentRail.Venmo, "VN-1234", hogs.Sams, DateTimeOffset.UtcNow));

        var statement = await Api.CreateClientFor(hogs.Sam).GetFromJsonAsync<AccountStatement>($"/leagues/{hogs.LeagueId}/accounts/{hogs.AccountOf(hogs.Sams)}");

        statement.ShouldNotBeNull();
        statement.MemberId.ShouldBe(hogs.Sams);
        statement.TeamName.ShouldBe("Sam's Slammers");
        statement.Yours.ShouldBeTrue();
        statement.Season.ShouldBe("2026");
        statement.Balance.ShouldBe(50m);
        statement.Totals.ShouldBe(new StatementTotals(Assessed: 50m, Confirmed: 0m));
        statement.Version.ShouldBe(5);
        statement.Lines.Select(l => (l.Kind, l.Amount, l.Memo, l.DueDate, l.Rail, l.Reference, l.Status, l.Reason, l.ByName)).ShouldBe(
        [
            (StatementLineKind.Assessment, 50m, "Season dues", DueDate, null, null, null, null, "Jacob"),
            (StatementLineKind.Attestation, 50m, null, null, "Zelle", "ZL-0000", "Rejected", "No Zelle with that reference arrived", "Sam"),
            (StatementLineKind.Attestation, 50m, null, null, "Venmo", "VN-1234", "Pending", null, "Sam"),
        ]);
    }

    [Fact]
    public async Task An_overpaid_account_has_a_negative_balance()
    {
        var hogs = await HollandHogs.OpenSeason(this);
        var paid = Guid.NewGuid();
        await Record(hogs, hogs.Sams,
            new PaymentAttested(paid, 60m, PaymentRail.Cash, null, hogs.Sams, DateTimeOffset.UtcNow),
            new PaymentConfirmed(paid, hogs.Jacobs, DateTimeOffset.UtcNow));

        var statement = await Api.CreateClientFor(hogs.Sam).GetFromJsonAsync<AccountStatement>($"/leagues/{hogs.LeagueId}/accounts/{hogs.AccountOf(hogs.Sams)}");

        statement.ShouldNotBeNull().Balance.ShouldBe(-10m);
        statement.Totals.ShouldBe(new StatementTotals(Assessed: 50m, Confirmed: 60m));
    }

    [Fact]
    public async Task A_member_is_forbidden_another_members_statement()
    {
        var hogs = await HollandHogs.OpenSeason(this);

        var response = await Api.CreateClientFor(hogs.Sam).GetAsync($"/leagues/{hogs.LeagueId}/accounts/{hogs.AccountOf(hogs.Jacobs)}");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_treasurer_reads_any_members_statement()
    {
        var hogs = await HollandHogs.OpenSeason(this);

        var statement = await Api.CreateClientFor(hogs.Jacob).GetFromJsonAsync<AccountStatement>($"/leagues/{hogs.LeagueId}/accounts/{hogs.AccountOf(hogs.Sams)}");

        statement.ShouldNotBeNull().MemberId.ShouldBe(hogs.Sams);
        statement.Yours.ShouldBeFalse();
    }

    [Fact]
    public async Task An_unknown_account_is_not_found()
    {
        var hogs = await HollandHogs.OpenSeason(this);

        var response = await Api.CreateClientFor(hogs.Jacob).GetAsync($"/leagues/{hogs.LeagueId}/accounts/{Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_statement_in_a_league_the_caller_is_not_in_is_forbidden()
    {
        var hogs = await HollandHogs.OpenSeason(this);
        var someoneElses = await HollandHogs.OpenSeason(this);

        var response = await Api.CreateClientFor(hogs.Jacob).GetAsync(
            $"/leagues/{someoneElses.LeagueId}/accounts/{someoneElses.AccountOf(someoneElses.Jacobs)}");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // Written straight to the account's stream, the way a command about it will append: attesting and
    // confirming over HTTP come later (#40, #41).
    private async Task Record(HollandHogs hogs, Guid memberId, params object[] events)
    {
        await using var session = Store.LightweightSession(hogs.LeagueId.ToString());
        var stream = await session.Events.FetchForWriting<MemberAccount>(hogs.AccountOf(memberId));
        stream.AppendMany(events);
        await session.SaveChangesAsync();
    }

    /// <summary>
    /// Holland Hogs as imported by Jacob, who keeps its books, with Sam holding Sam's Slammers and the
    /// 2026 season open at $50.
    /// </summary>
    private sealed record HollandHogs(Guid LeagueId, string Jacob, Guid Jacobs, string Sam, Guid Sams)
    {
        public Guid AccountOf(Guid memberId) => SeasonIds.AccountId(SeasonIds.SeasonId(LeagueId, "2026"), memberId);

        public static async Task<HollandHogs> OpenSeason(LedgerTests tests)
        {
            var leagueId = Guid.NewGuid();
            var jacob = NewSubject();
            var jacobsClient = tests.Api.CreateClientFor(jacob);
            (await jacobsClient.PostAsJsonAsync(
                $"/leagues/{leagueId}/import",
                new ImportLeagueRequest(tests.Api.Sleeper.CopyOfHollandHogs(), "jacob", "Jacob"))).StatusCode.ShouldBe(HttpStatusCode.Created);

            var sam = NewSubject();
            var league = await tests.ClaimAsync(leagueId, rosterId: 2, sam, "Sam");

            (await jacobsClient.PostAsJsonAsync(
                $"/leagues/{leagueId}/seasons",
                new OpenSeasonRequest("2026", 50m, DueDate))).StatusCode.ShouldBe(HttpStatusCode.Created);

            return new HollandHogs(
                leagueId,
                jacob,
                league.Members.Single(m => m.SleeperRosterId == 1).MemberId,
                sam,
                league.Members.Single(m => m.SleeperRosterId == 2).MemberId);
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

using System.Net;
using System.Net.Http.Json;
using BallBank.Api.Features.Treasury;

namespace BallBank.Integration.Tests.Http;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class DashboardTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset TenDaysLate = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    private BallBankApi Api => postgres.Api;

    [Fact]
    public async Task The_dashboard_adds_up_a_mixed_history()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var desk = new TreasurersDesk(Api, hogs);
        await desk.MixedHistory();
        await desk.Settled();

        var dashboard = await Read(hogs, hogs.Jacob);

        dashboard.Season.ShouldBe("2026");
        dashboard.Figures.ShouldBe(new DashboardFigures(
            Assessed: 225m,
            Confirmed: 120m,
            Refunded: 10m,
            Adjusted: 5m,
            Pot: 110m,
            Outstanding: 115m,
            Owed: 5m,
            PendingAttestations: 1));
    }

    [Fact]
    public async Task The_dashboard_says_how_current_it_is()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var desk = new TreasurersDesk(Api, hogs);
        await desk.MixedHistory();
        await desk.Settled();

        var dashboard = await Read(hogs, hogs.Jacob);

        dashboard.AsOf.ShouldNotBeNull().ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public async Task Delinquents_are_the_members_who_still_owe_after_the_due_date_most_overdue_first()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var desk = new TreasurersDesk(Api, hogs);
        await desk.MixedHistory();
        await desk.Settled();

        using var _ = Api.Clock.Set(TenDaysLate);
        var dashboard = await Read(hogs, hogs.Jacob);

        // The two members who have paid nothing, then Jacob, who owes a little; Sam, whom the pot owes, is not one.
        dashboard.Delinquents.Length.ShouldBe(3);
        dashboard.Delinquents.ShouldAllBe(d => d.DaysOverdue == 10 && d.EarliestDueDate == HollandHogsSeason.DueDate);
        dashboard.Delinquents.Select(d => d.Balance).ShouldBe([50m, 50m, 15m]);
        dashboard.Delinquents.Last().ShouldSatisfyAllConditions(
            d => d.MemberId.ShouldBe(hogs.Jacobs),
            d => d.AccountId.ShouldBe(hogs.AccountOf(hogs.Jacobs)),
            d => d.TeamName.ShouldNotBeNullOrEmpty(),
            d => d.DisplayName.ShouldBe("Jacob"));
        dashboard.Delinquents.ShouldNotContain(d => d.MemberId == hogs.Sams);
    }

    [Fact]
    public async Task Nobody_is_delinquent_on_the_due_date_itself()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var desk = new TreasurersDesk(Api, hogs);
        await desk.Settled();

        using var _ = Api.Clock.Set(new DateTimeOffset(HollandHogsSeason.DueDate.ToDateTime(new TimeOnly(23, 59)), TimeSpan.Zero));
        var dashboard = await Read(hogs, hogs.Jacob);

        dashboard.Delinquents.ShouldBeEmpty();
        dashboard.Figures.Outstanding.ShouldBe(200m);
    }

    [Fact]
    public async Task A_member_cannot_read_the_dashboard()
    {
        var hogs = await HollandHogsSeason.Open(Api);

        var response = await Api.CreateClientFor(hogs.Sam).GetAsync($"/leagues/{hogs.LeagueId}/seasons/2026/dashboard");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_season_that_was_never_opened_has_no_dashboard()
    {
        var hogs = await HollandHogsSeason.Open(Api);

        var response = await Api.CreateClientFor(hogs.Jacob).GetAsync($"/leagues/{hogs.LeagueId}/seasons/2031/dashboard");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<Dashboard> Read(HollandHogsSeason hogs, string subject) =>
        (await Api.CreateClientFor(subject).GetFromJsonAsync<Dashboard>($"/leagues/{hogs.LeagueId}/seasons/2026/dashboard")).ShouldNotBeNull();
}

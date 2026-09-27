using System.Net;
using System.Net.Http.Json;
using BallBank.Api;
using BallBank.Api.Features.Treasury;
using BallBank.Domain.Treasury;

namespace BallBank.Integration.Tests.Http;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class AccountHistoryTests(PostgresFixture postgres)
{
    // An account's stream after the season opened: AccountOpened, then the season dues.
    private const int OpenedVersion = 2;

    private BallBankApi Api => postgres.Api;

    [Fact]
    public async Task A_member_reads_every_event_of_their_account_in_league_language()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var account = $"/leagues/{hogs.LeagueId}/accounts/{hogs.AccountOf(hogs.Sams)}";
        var venmo = Guid.NewGuid();
        var cash = Guid.NewGuid();
        await Send(hogs.Sam, $"{account}/attestations", new AttestPaymentRequest(venmo, 50m, PaymentRail.Venmo, "VN-1234", OpenedVersion));
        await Send(hogs.Jacob, $"{account}/attestations/{venmo}/confirmation", new ConfirmPaymentRequest(OpenedVersion + 1));
        await Send(hogs.Sam, $"{account}/attestations", new AttestPaymentRequest(cash, 10m, PaymentRail.Cash, null, OpenedVersion + 2));
        await Send(hogs.Jacob, $"{account}/attestations/{cash}/rejection", new RejectPaymentRequest("No cash came in", OpenedVersion + 3));
        await Send(hogs.Jacob, $"{account}/adjustments", new PostAdjustmentRequest(Guid.NewGuid(), 10m, "Late fee", OpenedVersion + 4));
        await Send(hogs.Jacob, $"{account}/adjustments", new PostAdjustmentRequest(Guid.NewGuid(), -5m, "Waived part of the fee", OpenedVersion + 5));

        var history = await Api.CreateClientFor(hogs.Sam).GetFromJsonAsync<AccountHistoryEntry[]>($"{account}/history");

        history.ShouldNotBeNull().Select(e => e.Sentence).ShouldBe([
            "Jacob opened Sam's account for the 2026 season",
            "Jacob assessed $50.00 for Season dues, due Oct 1, 2026",
            "Sam attested a $50.00 Venmo payment VN-1234",
            "Jacob confirmed the $50.00 Venmo payment VN-1234",
            "Sam attested a $10.00 Cash payment",
            "Jacob rejected the $10.00 Cash payment: No cash came in",
            "Jacob raised the balance by $10.00: Late fee",
            "Jacob lowered the balance by $5.00: Waived part of the fee",
        ]);
        history.Select(e => e.Version).ShouldBe([1L, 2, 3, 4, 5, 6, 7, 8]);
        history.Select(e => e.Sequence).ShouldBeInOrder(SortDirection.Ascending);
        history.Select(e => e.Type).ShouldBe([
            nameof(AccountOpened), nameof(DuesAssessed), nameof(PaymentAttested), nameof(PaymentConfirmed),
            nameof(PaymentAttested), nameof(PaymentRejected), nameof(AdjustmentPosted), nameof(AdjustmentPosted),
        ]);
        history.Select(e => e.ByName).ShouldBe(["Jacob", "Jacob", "Sam", "Jacob", "Sam", "Jacob", "Jacob", "Jacob"]);
        history[2].By.ShouldBe(hogs.Sams);
        history[3].By.ShouldBe(hogs.Jacobs);
        history.ShouldAllBe(e => e.At > DateTimeOffset.UtcNow.AddMinutes(-5) && e.At <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task A_refund_reads_as_money_paid_back_out_of_the_pot()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var account = $"/leagues/{hogs.LeagueId}/accounts/{hogs.AccountOf(hogs.Sams)}";
        var attestationId = Guid.NewGuid();
        await Send(hogs.Sam, $"{account}/attestations", new AttestPaymentRequest(attestationId, 60m, PaymentRail.Cash, null, OpenedVersion));
        await Send(hogs.Jacob, $"{account}/attestations/{attestationId}/confirmation", new ConfirmPaymentRequest(OpenedVersion + 1));
        await Send(hogs.Jacob, $"{account}/adjustments", new PostAdjustmentRequest(Guid.NewGuid(), 10m, "Refunded the $10 overpaid", OpenedVersion + 2, Refund: true));

        var history = await Api.CreateClientFor(hogs.Jacob).GetFromJsonAsync<AccountHistoryEntry[]>($"{account}/history");

        history.ShouldNotBeNull().Last().Sentence.ShouldBe("Jacob refunded $10.00 out of the pot: Refunded the $10 overpaid");
    }

    [Fact]
    public async Task A_treasurer_reads_any_members_history()
    {
        var hogs = await HollandHogsSeason.Open(Api);

        var response = await Api.CreateClientFor(hogs.Jacob).GetAsync($"/leagues/{hogs.LeagueId}/accounts/{hogs.AccountOf(hogs.Sams)}/history");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<AccountHistoryEntry[]>()).ShouldNotBeNull().Length.ShouldBe(OpenedVersion);
    }

    [Fact]
    public async Task Another_members_history_is_forbidden()
    {
        var hogs = await HollandHogsSeason.Open(Api);

        var response = await Api.CreateClientFor(hogs.Sam).GetAsync($"/leagues/{hogs.LeagueId}/accounts/{hogs.AccountOf(hogs.Jacobs)}/history");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_history_of_an_unknown_account_is_not_found()
    {
        var hogs = await HollandHogsSeason.Open(Api);

        var response = await Api.CreateClientFor(hogs.Jacob).GetAsync($"/leagues/{hogs.LeagueId}/accounts/{Guid.NewGuid()}/history");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task Send(string subject, string path, object body)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body, body.GetType()) };
        message.Headers.Add(Idempotency.Header, Guid.NewGuid().ToString());
        (await Api.CreateClientFor(subject).SendAsync(message)).IsSuccessStatusCode.ShouldBeTrue();
    }
}

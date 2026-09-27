using System.Net;
using System.Net.Http.Json;
using BallBank.Api;
using BallBank.Api.Features.Treasury;
using BallBank.Domain.Treasury;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace BallBank.Integration.Tests.Http;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class PostingAnAdjustmentTests(PostgresFixture postgres)
{
    // An account's stream after the season opened: AccountOpened, then the season dues.
    private const int OpenedVersion = 2;

    private BallBankApi Api => postgres.Api;
    private IDocumentStore Store => Api.Services.GetRequiredService<IDocumentStore>();

    [Fact]
    public async Task A_treasurer_posts_a_waiver_and_the_statement_shows_why_and_who()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var adjustmentId = Guid.NewGuid();

        var response = await Adjust(hogs.Jacob, hogs, hogs.Sams, new PostAdjustmentRequest(adjustmentId, -20m, " Waived: hosted the draft ", OpenedVersion));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var statement = (await response.Content.ReadFromJsonAsync<AccountStatement>()).ShouldNotBeNull();
        statement.Balance.ShouldBe(30m);
        statement.Totals.ShouldBe(new StatementTotals(Assessed: 50m, Confirmed: 0m, Adjusted: -20m));
        statement.Version.ShouldBe(OpenedVersion + 1);
        statement.Lines.Last().ShouldSatisfyAllConditions(
            line => line.Kind.ShouldBe(StatementLineKind.Adjustment),
            line => line.Id.ShouldBe(adjustmentId),
            line => line.Amount.ShouldBe(-20m),
            line => line.Reason.ShouldBe("Waived: hosted the draft"),
            line => line.Refund.ShouldBeFalse(),
            line => line.By.ShouldBe(hogs.Jacobs),
            line => line.ByName.ShouldBe("Jacob"));

        var read = await Api.CreateClientFor(hogs.Sam).GetFromJsonAsync<AccountStatement>($"/leagues/{hogs.LeagueId}/accounts/{hogs.AccountOf(hogs.Sams)}");
        read.ShouldNotBeNull().Balance.ShouldBe(30m);
        read.Lines.Last().Reason.ShouldBe("Waived: hosted the draft");

        var ledger = await Api.CreateClientFor(hogs.Sam).GetFromJsonAsync<LedgerEntry[]>($"/leagues/{hogs.LeagueId}/seasons/2026/ledger");
        ledger.ShouldNotBeNull().Single(e => e.MemberId == hogs.Sams).Balance.ShouldBe(30m);
    }

    [Fact]
    public async Task A_positive_adjustment_raises_the_balance()
    {
        var hogs = await HollandHogsSeason.Open(Api);

        var response = await Adjust(hogs.Jacob, hogs, hogs.Sams, new PostAdjustmentRequest(Guid.NewGuid(), 10m, "Late fee", OpenedVersion));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<AccountStatement>()).ShouldNotBeNull().Balance.ShouldBe(60m);
    }

    [Fact]
    public async Task Posting_the_same_adjustment_again_records_it_once()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var request = new PostAdjustmentRequest(Guid.NewGuid(), -20m, "Waived", OpenedVersion);
        (await Adjust(hogs.Jacob, hogs, hogs.Sams, request)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var again = await Adjust(hogs.Jacob, hogs, hogs.Sams, request);

        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await again.Content.ReadFromJsonAsync<AccountStatement>()).ShouldNotBeNull().Balance.ShouldBe(30m);
        (await StreamVersionOf(hogs, hogs.Sams)).ShouldBe(OpenedVersion + 1);
    }

    [Fact]
    public async Task An_adjustment_without_a_reason_is_refused()
    {
        var hogs = await HollandHogsSeason.Open(Api);

        var response = await Adjust(hogs.Jacob, hogs, hogs.Sams, new PostAdjustmentRequest(Guid.NewGuid(), -20m, null, OpenedVersion));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>()).ShouldNotBeNull()
            .Detail.ShouldBe("An adjustment needs a reason the member will see.");
        (await StreamVersionOf(hogs, hogs.Sams)).ShouldBe(OpenedVersion);
    }

    [Fact]
    public async Task A_member_cannot_post_an_adjustment()
    {
        var hogs = await HollandHogsSeason.Open(Api);

        var response = await Adjust(hogs.Sam, hogs, hogs.Sams, new PostAdjustmentRequest(Guid.NewGuid(), -50m, "Short this month", OpenedVersion));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await StreamVersionOf(hogs, hogs.Sams)).ShouldBe(OpenedVersion);
    }

    [Fact]
    public async Task Adjusting_against_a_stale_version_is_a_version_conflict()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        (await Adjust(hogs.Jacob, hogs, hogs.Sams, new PostAdjustmentRequest(Guid.NewGuid(), -5m, "Waived part", OpenedVersion)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await Adjust(hogs.Jacob, hogs, hogs.Sams, new PostAdjustmentRequest(Guid.NewGuid(), -5m, "Waived part again", OpenedVersion));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>()).ShouldNotBeNull().Type.ShouldBe(ExpectedVersion.ConflictType);
        (await StreamVersionOf(hogs, hogs.Sams)).ShouldBe(OpenedVersion + 1);
    }

    [Fact]
    public async Task Adjusting_without_a_version_is_a_precondition_failure()
    {
        var hogs = await HollandHogsSeason.Open(Api);

        var response = await Adjust(hogs.Jacob, hogs, hogs.Sams, new PostAdjustmentRequest(Guid.NewGuid(), -5m, "Waived part", Version: null));

        response.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
    }

    [Fact]
    public async Task Refunding_an_overpayment_brings_the_balance_to_zero()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var attestationId = Guid.NewGuid();
        await Send(hogs.Sam, $"/leagues/{hogs.LeagueId}/accounts/{hogs.AccountOf(hogs.Sams)}/attestations",
            new AttestPaymentRequest(attestationId, 60m, PaymentRail.Cash, null, OpenedVersion));
        (await Send(hogs.Jacob, $"/leagues/{hogs.LeagueId}/accounts/{hogs.AccountOf(hogs.Sams)}/attestations/{attestationId}/confirmation",
            new ConfirmPaymentRequest(OpenedVersion + 1))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await Adjust(hogs.Jacob, hogs, hogs.Sams, new PostAdjustmentRequest(Guid.NewGuid(), 10m, "Refunded the $10 overpaid", OpenedVersion + 2, Refund: true));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var statement = (await response.Content.ReadFromJsonAsync<AccountStatement>()).ShouldNotBeNull();
        statement.Balance.ShouldBe(0m);
        statement.Lines.Last().ShouldSatisfyAllConditions(
            line => line.Kind.ShouldBe(StatementLineKind.Adjustment),
            line => line.Refund.ShouldBeTrue());
    }

    [Fact]
    public async Task A_refund_that_lowers_the_balance_is_refused()
    {
        var hogs = await HollandHogsSeason.Open(Api);

        var response = await Adjust(hogs.Jacob, hogs, hogs.Sams, new PostAdjustmentRequest(Guid.NewGuid(), -10m, "Refunded", OpenedVersion, Refund: true));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>()).ShouldNotBeNull()
            .Detail.ShouldBe("A refund pays the member back, so it must raise the balance.");
    }

    private async Task<long> StreamVersionOf(HollandHogsSeason hogs, Guid memberId)
    {
        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        return (await session.Events.FetchStreamStateAsync(hogs.AccountOf(memberId))).ShouldNotBeNull().Version;
    }

    private Task<HttpResponseMessage> Adjust(string subject, HollandHogsSeason hogs, Guid memberId, PostAdjustmentRequest request) =>
        Send(subject, $"/leagues/{hogs.LeagueId}/accounts/{hogs.AccountOf(memberId)}/adjustments", request);

    private Task<HttpResponseMessage> Send(string subject, string path, object body)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body, body.GetType()) };
        message.Headers.Add(Idempotency.Header, Guid.NewGuid().ToString());
        return Api.CreateClientFor(subject).SendAsync(message);
    }
}

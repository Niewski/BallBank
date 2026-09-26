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
public class ConfirmationQueueTests(PostgresFixture postgres)
{
    // An account's stream after the season opened: AccountOpened, then the season dues.
    private const int OpenedVersion = 2;

    private BallBankApi Api => postgres.Api;
    private IDocumentStore Store => Api.Services.GetRequiredService<IDocumentStore>();

    [Fact]
    public async Task The_queue_lists_every_pending_attestation_of_the_season_oldest_first()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var sams = await Attest(hogs.Sam, hogs, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1234", OpenedVersion);
        var jacobs = await Attest(hogs.Jacob, hogs, hogs.Jacobs, 50m, PaymentRail.Cash, null, OpenedVersion);
        var settled = await Attest(hogs.Jacob, hogs, hogs.Sams, 10m, PaymentRail.Cash, null, OpenedVersion + 1);
        (await Confirm(hogs.Jacob, hogs, hogs.Sams, settled, OpenedVersion + 2)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var queue = await Api.CreateClientFor(hogs.Jacob)
            .GetFromJsonAsync<ConfirmationQueueEntry[]>($"/leagues/{hogs.LeagueId}/seasons/2026/confirmations");

        queue.ShouldNotBeNull().Select(e => e.AttestationId).ShouldBe([sams, jacobs]);
        queue[0].ShouldSatisfyAllConditions(
            entry => entry.AccountId.ShouldBe(hogs.AccountOf(hogs.Sams)),
            entry => entry.MemberId.ShouldBe(hogs.Sams),
            entry => entry.TeamName.ShouldBe("Sam's Slammers"),
            entry => entry.DisplayName.ShouldBe("Sam"),
            entry => entry.Amount.ShouldBe(50m),
            entry => entry.Rail.ShouldBe("Venmo"),
            entry => entry.Reference.ShouldBe("VN-1234"),
            entry => entry.AttestedByName.ShouldBe("Sam"),
            entry => entry.Version.ShouldBe(OpenedVersion + 3));
        queue[0].AttestedAt.ShouldBeLessThan(queue[1].AttestedAt);
    }

    [Fact]
    public async Task A_member_cannot_read_the_queue()
    {
        var hogs = await HollandHogsSeason.Open(Api);

        var response = await Api.CreateClientFor(hogs.Sam).GetAsync($"/leagues/{hogs.LeagueId}/seasons/2026/confirmations");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Confirming_moves_the_balance_in_the_statement_it_answers_with()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var attestation = await Attest(hogs.Sam, hogs, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1234", OpenedVersion);

        var response = await Confirm(hogs.Jacob, hogs, hogs.Sams, attestation, OpenedVersion + 1);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var statement = (await response.Content.ReadFromJsonAsync<AccountStatement>()).ShouldNotBeNull();
        statement.Balance.ShouldBe(0m);
        statement.Version.ShouldBe(OpenedVersion + 2);
        statement.Lines.Single(l => l.Id == attestation).Status.ShouldBe("Confirmed");

        var read = await Api.CreateClientFor(hogs.Sam).GetFromJsonAsync<AccountStatement>($"/leagues/{hogs.LeagueId}/accounts/{hogs.AccountOf(hogs.Sams)}");
        read.ShouldNotBeNull().Balance.ShouldBe(0m);
    }

    [Fact]
    public async Task Confirming_again_changes_nothing()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var attestation = await Attest(hogs.Sam, hogs, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1234", OpenedVersion);
        (await Confirm(hogs.Jacob, hogs, hogs.Sams, attestation, OpenedVersion + 1)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var again = await Confirm(hogs.Jacob, hogs, hogs.Sams, attestation, OpenedVersion + 1);

        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await again.Content.ReadFromJsonAsync<AccountStatement>()).ShouldNotBeNull().Balance.ShouldBe(0m);
        (await StreamVersionOf(hogs, hogs.Sams)).ShouldBe(OpenedVersion + 2);
    }

    [Fact]
    public async Task A_treasurer_confirms_their_own_attestation()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var attestation = await Attest(hogs.Jacob, hogs, hogs.Jacobs, 50m, PaymentRail.Cash, null, OpenedVersion);

        var response = await Confirm(hogs.Jacob, hogs, hogs.Jacobs, attestation, OpenedVersion + 1);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<AccountStatement>()).ShouldNotBeNull().Balance.ShouldBe(0m);
    }

    [Fact]
    public async Task Rejecting_shows_the_member_the_reason_and_leaves_the_balance()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var attestation = await Attest(hogs.Sam, hogs, hogs.Sams, 50m, PaymentRail.Zelle, "ZL-0000", OpenedVersion);

        var response = await Reject(hogs.Jacob, hogs, hogs.Sams, attestation, " No Zelle with that reference arrived ", OpenedVersion + 1);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<AccountStatement>()).ShouldNotBeNull().Version.ShouldBe(OpenedVersion + 2);

        var read = await Api.CreateClientFor(hogs.Sam).GetFromJsonAsync<AccountStatement>($"/leagues/{hogs.LeagueId}/accounts/{hogs.AccountOf(hogs.Sams)}");
        read.ShouldNotBeNull().Balance.ShouldBe(50m);
        read.Lines.Single(l => l.Id == attestation).ShouldSatisfyAllConditions(
            line => line.Status.ShouldBe("Rejected"),
            line => line.Reason.ShouldBe("No Zelle with that reference arrived"));
    }

    [Fact]
    public async Task Rejecting_without_a_reason_is_refused()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var attestation = await Attest(hogs.Sam, hogs, hogs.Sams, 50m, PaymentRail.Zelle, "ZL-0000", OpenedVersion);

        var response = await Reject(hogs.Jacob, hogs, hogs.Sams, attestation, reason: null, OpenedVersion + 1);

        await ShouldBeRefused(response, "Rejecting a payment needs a reason the member will see.");
        (await StreamVersionOf(hogs, hogs.Sams)).ShouldBe(OpenedVersion + 1);
    }

    [Fact]
    public async Task Rejecting_a_confirmed_payment_is_refused()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var attestation = await Attest(hogs.Sam, hogs, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1234", OpenedVersion);
        (await Confirm(hogs.Jacob, hogs, hogs.Sams, attestation, OpenedVersion + 1)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await Reject(hogs.Jacob, hogs, hogs.Sams, attestation, "Wrong account", OpenedVersion + 2);

        await ShouldBeRefused(response, "A confirmed payment cannot be rejected; post an adjustment instead.");
    }

    [Fact]
    public async Task Confirming_a_rejected_payment_is_refused()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var attestation = await Attest(hogs.Sam, hogs, hogs.Sams, 50m, PaymentRail.Zelle, "ZL-0000", OpenedVersion);
        (await Reject(hogs.Jacob, hogs, hogs.Sams, attestation, "Nothing arrived", OpenedVersion + 1)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await Confirm(hogs.Jacob, hogs, hogs.Sams, attestation, OpenedVersion + 2);

        await ShouldBeRefused(response, "A rejected attestation cannot be confirmed; ask the member to attest the payment again.");
    }

    [Fact]
    public async Task A_member_cannot_confirm_or_reject()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var attestation = await Attest(hogs.Sam, hogs, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1234", OpenedVersion);

        (await Confirm(hogs.Sam, hogs, hogs.Sams, attestation, OpenedVersion + 1)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Reject(hogs.Sam, hogs, hogs.Sams, attestation, "Never mind", OpenedVersion + 1)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await StreamVersionOf(hogs, hogs.Sams)).ShouldBe(OpenedVersion + 1);
    }

    [Fact]
    public async Task Confirming_without_a_version_is_a_precondition_failure()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var attestation = await Attest(hogs.Sam, hogs, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1234", OpenedVersion);

        var response = await Confirm(hogs.Jacob, hogs, hogs.Sams, attestation, version: null);

        response.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await StreamVersionOf(hogs, hogs.Sams)).ShouldBe(OpenedVersion + 1);
    }

    [Fact]
    public async Task Confirming_against_a_stale_version_is_a_version_conflict()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var attestation = await Attest(hogs.Sam, hogs, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1234", OpenedVersion);
        await Attest(hogs.Sam, hogs, hogs.Sams, 5m, PaymentRail.Cash, null, OpenedVersion + 1);

        var response = await Confirm(hogs.Jacob, hogs, hogs.Sams, attestation, OpenedVersion + 1);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>()).ShouldNotBeNull().Type.ShouldBe(ExpectedVersion.ConflictType);
        (await StreamVersionOf(hogs, hogs.Sams)).ShouldBe(OpenedVersion + 2);
    }

    [Fact]
    public async Task A_treasurer_reading_a_statement_is_told_they_can_settle_it()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var path = $"/leagues/{hogs.LeagueId}/accounts/{hogs.AccountOf(hogs.Sams)}";

        (await Api.CreateClientFor(hogs.Jacob).GetFromJsonAsync<AccountStatement>(path)).ShouldNotBeNull().YouAreTreasurer.ShouldBeTrue();
        (await Api.CreateClientFor(hogs.Sam).GetFromJsonAsync<AccountStatement>(path)).ShouldNotBeNull().YouAreTreasurer.ShouldBeFalse();
    }

    private static async Task ShouldBeRefused(HttpResponseMessage response, string reason)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>()).ShouldNotBeNull().Detail.ShouldBe(reason);
    }

    private async Task<long> StreamVersionOf(HollandHogsSeason hogs, Guid memberId)
    {
        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        return (await session.Events.FetchStreamStateAsync(hogs.AccountOf(memberId))).ShouldNotBeNull().Version;
    }

    private async Task<Guid> Attest(string subject, HollandHogsSeason hogs, Guid memberId, decimal amount, PaymentRail rail, string? reference, int version)
    {
        var attestationId = Guid.NewGuid();
        var response = await Send(subject, $"/leagues/{hogs.LeagueId}/accounts/{hogs.AccountOf(memberId)}/attestations",
            new AttestPaymentRequest(attestationId, amount, rail, reference, version));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return attestationId;
    }

    private Task<HttpResponseMessage> Confirm(string subject, HollandHogsSeason hogs, Guid memberId, Guid attestationId, int? version) =>
        Send(subject, $"/leagues/{hogs.LeagueId}/accounts/{hogs.AccountOf(memberId)}/attestations/{attestationId}/confirmation",
            new ConfirmPaymentRequest(version));

    private Task<HttpResponseMessage> Reject(string subject, HollandHogsSeason hogs, Guid memberId, Guid attestationId, string? reason, int? version) =>
        Send(subject, $"/leagues/{hogs.LeagueId}/accounts/{hogs.AccountOf(memberId)}/attestations/{attestationId}/rejection",
            new RejectPaymentRequest(reason, version));

    private Task<HttpResponseMessage> Send(string subject, string path, object body)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body, body.GetType()) };
        message.Headers.Add(Idempotency.Header, Guid.NewGuid().ToString());
        return Api.CreateClientFor(subject).SendAsync(message);
    }
}

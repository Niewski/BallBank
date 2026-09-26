using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BallBank.Api;
using BallBank.Api.Features.Treasury;
using BallBank.Domain.Treasury;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace BallBank.Integration.Tests.Http;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class AttestingAPaymentTests(PostgresFixture postgres)
{
    // An account's stream after the season opened: AccountOpened, then the season dues.
    private const int OpenedVersion = 2;

    private BallBankApi Api => postgres.Api;
    private IDocumentStore Store => Api.Services.GetRequiredService<IDocumentStore>();

    [Fact]
    public async Task A_member_attests_a_payment_and_sees_it_pending_with_the_balance_unchanged()
    {
        var hogs = await HollandHogsSeason.Open(Api);

        var response = await Attest(hogs.Sam, hogs, hogs.Sams, new AttestPaymentRequest(Guid.NewGuid(), 50m, PaymentRail.Venmo, "VN-1234", OpenedVersion));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var statement = (await response.Content.ReadFromJsonAsync<AccountStatement>()).ShouldNotBeNull();
        statement.Balance.ShouldBe(50m);
        statement.Version.ShouldBe(OpenedVersion + 1);
        statement.Lines.Last().ShouldSatisfyAllConditions(
            line => line.Kind.ShouldBe(StatementLineKind.Attestation),
            line => line.Amount.ShouldBe(50m),
            line => line.Rail.ShouldBe("Venmo"),
            line => line.Reference.ShouldBe("VN-1234"),
            line => line.Status.ShouldBe("Pending"),
            line => line.By.ShouldBe(hogs.Sams),
            line => line.ByName.ShouldBe("Sam"));

        var read = await Api.CreateClientFor(hogs.Sam).GetFromJsonAsync<AccountStatement>($"/leagues/{hogs.LeagueId}/accounts/{hogs.AccountOf(hogs.Sams)}");
        read.ShouldNotBeNull().Version.ShouldBe(statement.Version);
        read.Lines.Count.ShouldBe(statement.Lines.Count);
    }

    [Fact]
    public async Task Cash_needs_no_reference()
    {
        var hogs = await HollandHogsSeason.Open(Api);

        var response = await Attest(hogs.Sam, hogs, hogs.Sams, new AttestPaymentRequest(Guid.NewGuid(), 20m, PaymentRail.Cash, null, OpenedVersion));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Any_other_rail_needs_a_reference()
    {
        var hogs = await HollandHogsSeason.Open(Api);

        var response = await Attest(hogs.Sam, hogs, hogs.Sams, new AttestPaymentRequest(Guid.NewGuid(), 50m, PaymentRail.Zelle, " ", OpenedVersion));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>()).ShouldNotBeNull()
            .Detail.ShouldBe("A Zelle payment needs a reference so the treasurer can find it.");
    }

    [Fact]
    public async Task A_payment_in_fractions_of_a_cent_is_refused()
    {
        var hogs = await HollandHogsSeason.Open(Api);

        var response = await Attest(hogs.Sam, hogs, hogs.Sams, new AttestPaymentRequest(Guid.NewGuid(), 50.005m, PaymentRail.Cash, null, OpenedVersion));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>()).ShouldNotBeNull()
            .Detail.ShouldBe("A payment must be in whole cents.");
    }

    [Fact]
    public async Task A_treasurer_attests_cash_a_member_handed_them()
    {
        var hogs = await HollandHogsSeason.Open(Api);

        var response = await Attest(hogs.Jacob, hogs, hogs.Sams, new AttestPaymentRequest(Guid.NewGuid(), 50m, PaymentRail.Cash, null, OpenedVersion));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var statement = (await response.Content.ReadFromJsonAsync<AccountStatement>()).ShouldNotBeNull();
        statement.Yours.ShouldBeFalse();
        statement.Lines.Last().By.ShouldBe(hogs.Jacobs);
    }

    [Fact]
    public async Task A_member_attesting_on_another_members_account_is_forbidden()
    {
        var hogs = await HollandHogsSeason.Open(Api);

        var response = await Attest(hogs.Sam, hogs, hogs.Jacobs, new AttestPaymentRequest(Guid.NewGuid(), 50m, PaymentRail.Cash, null, OpenedVersion));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await StreamVersionOf(hogs, hogs.Jacobs)).ShouldBe(OpenedVersion);
    }

    [Fact]
    public async Task Attesting_on_an_unknown_account_is_not_found()
    {
        var hogs = await HollandHogsSeason.Open(Api);

        var response = await Send(Api.CreateClientFor(hogs.Jacob), hogs.LeagueId, Guid.NewGuid(), new AttestPaymentRequest(Guid.NewGuid(), 50m, PaymentRail.Cash, null, 1));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Attesting_without_an_idempotency_key_is_a_precondition_failure()
    {
        var hogs = await HollandHogsSeason.Open(Api);

        var response = await Api.CreateClientFor(hogs.Sam).PostAsJsonAsync(
            $"/leagues/{hogs.LeagueId}/accounts/{hogs.AccountOf(hogs.Sams)}/attestations",
            new AttestPaymentRequest(Guid.NewGuid(), 50m, PaymentRail.Cash, null, OpenedVersion));

        response.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
    }

    [Fact]
    public async Task Attesting_without_a_version_is_a_precondition_failure()
    {
        var hogs = await HollandHogsSeason.Open(Api);

        var response = await Attest(hogs.Sam, hogs, hogs.Sams, new AttestPaymentRequest(Guid.NewGuid(), 50m, PaymentRail.Cash, null, Version: null));

        response.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>()).ShouldNotBeNull().Title.ShouldBe("Version required");
        (await StreamVersionOf(hogs, hogs.Sams)).ShouldBe(OpenedVersion);
    }

    [Fact]
    public async Task Attesting_against_a_stale_version_is_a_version_conflict_with_the_current_version()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        (await Attest(hogs.Jacob, hogs, hogs.Sams, new AttestPaymentRequest(Guid.NewGuid(), 50m, PaymentRail.Cash, null, OpenedVersion)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await Attest(hogs.Sam, hogs, hogs.Sams, new AttestPaymentRequest(Guid.NewGuid(), 50m, PaymentRail.Venmo, "VN-1234", OpenedVersion));

        await ShouldBeAVersionConflict(response, currentVersion: OpenedVersion + 1);
        (await StreamVersionOf(hogs, hogs.Sams)).ShouldBe(OpenedVersion + 1);
    }

    [Fact]
    public async Task Two_attestations_racing_on_one_version_record_one_and_refuse_the_other()
    {
        var hogs = await HollandHogsSeason.Open(Api);

        var responses = await Task.WhenAll(
            Attest(hogs.Sam, hogs, hogs.Sams, new AttestPaymentRequest(Guid.NewGuid(), 50m, PaymentRail.Venmo, "VN-1234", OpenedVersion)),
            Attest(hogs.Jacob, hogs, hogs.Sams, new AttestPaymentRequest(Guid.NewGuid(), 50m, PaymentRail.Cash, null, OpenedVersion)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        await ShouldBeAVersionConflict(responses.Single(r => r.StatusCode != HttpStatusCode.Created), currentVersion: OpenedVersion + 1);
        (await StreamVersionOf(hogs, hogs.Sams)).ShouldBe(OpenedVersion + 1);
    }

    [Fact]
    public async Task A_double_submission_records_one_attestation()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var request = new AttestPaymentRequest(Guid.NewGuid(), 50m, PaymentRail.Venmo, "VN-1234", OpenedVersion);
        var key = Guid.NewGuid().ToString();

        var first = await Attest(hogs.Sam, hogs, hogs.Sams, request, key);
        var again = await Attest(hogs.Sam, hogs, hogs.Sams, request, key);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        again.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await again.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());
        (await StreamVersionOf(hogs, hogs.Sams)).ShouldBe(OpenedVersion + 1);
    }

    [Fact]
    public async Task A_double_submission_in_flight_at_once_records_one_attestation()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var request = new AttestPaymentRequest(Guid.NewGuid(), 50m, PaymentRail.Venmo, "VN-1234", OpenedVersion);
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(Attest(hogs.Sam, hogs, hogs.Sams, request, key), Attest(hogs.Sam, hogs, hogs.Sams, request, key));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Created);
        (await responses[1].Content.ReadAsStringAsync()).ShouldBe(await responses[0].Content.ReadAsStringAsync());
        (await StreamVersionOf(hogs, hogs.Sams)).ShouldBe(OpenedVersion + 1);
    }

    [Fact]
    public async Task The_same_attestation_under_a_fresh_key_records_nothing_new()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var request = new AttestPaymentRequest(Guid.NewGuid(), 50m, PaymentRail.Venmo, "VN-1234", OpenedVersion);
        (await Attest(hogs.Sam, hogs, hogs.Sams, request)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var again = await Attest(hogs.Sam, hogs, hogs.Sams, request);

        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await again.Content.ReadFromJsonAsync<AccountStatement>()).ShouldNotBeNull()
            .Lines.Count(l => l.Kind == StatementLineKind.Attestation).ShouldBe(1);
        (await StreamVersionOf(hogs, hogs.Sams)).ShouldBe(OpenedVersion + 1);
    }

    [Fact]
    public async Task The_history_records_who_attested()
    {
        var hogs = await HollandHogsSeason.Open(Api);

        await Attest(hogs.Jacob, hogs, hogs.Sams, new AttestPaymentRequest(Guid.NewGuid(), 50m, PaymentRail.Cash, null, OpenedVersion));

        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        var stored = (await session.Events.FetchStreamAsync(hogs.AccountOf(hogs.Sams))).Last();
        ((PaymentAttested)stored.Data).AttestedBy.ShouldBe(hogs.Jacobs);
        stored.GetHeader(EventHeaders.Subject)?.ToString().ShouldBe(hogs.Jacob);
    }

    private static async Task ShouldBeAVersionConflict(HttpResponseMessage response, int currentVersion)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>()).ShouldNotBeNull();
        problem.Type.ShouldBe("version-conflict");
        ((JsonElement)problem.Extensions["currentVersion"]!).GetInt32().ShouldBe(currentVersion);
    }

    private async Task<long> StreamVersionOf(HollandHogsSeason hogs, Guid memberId)
    {
        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        return (await session.Events.FetchStreamStateAsync(hogs.AccountOf(memberId))).ShouldNotBeNull().Version;
    }

    private Task<HttpResponseMessage> Attest(string subject, HollandHogsSeason hogs, Guid memberId, AttestPaymentRequest request, string? idempotencyKey = null) =>
        Send(Api.CreateClientFor(subject), hogs.LeagueId, hogs.AccountOf(memberId), request, idempotencyKey);

    private static Task<HttpResponseMessage> Send(HttpClient client, Guid leagueId, Guid accountId, AttestPaymentRequest request, string? idempotencyKey = null)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, $"/leagues/{leagueId}/accounts/{accountId}/attestations")
        {
            Content = JsonContent.Create(request),
        };
        message.Headers.Add(Idempotency.Header, idempotencyKey ?? Guid.NewGuid().ToString());
        return client.SendAsync(message);
    }
}

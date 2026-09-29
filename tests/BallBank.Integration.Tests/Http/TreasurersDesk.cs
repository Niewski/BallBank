using System.Net;
using System.Net.Http.Json;
using BallBank.Api;
using BallBank.Api.Features.Treasury;
using BallBank.Domain.Treasury;
using Marten;
using Marten.Events;
using Microsoft.Extensions.DependencyInjection;

namespace BallBank.Integration.Tests.Http;

/// <summary>
/// The treasurer's commands over HTTP against a <see cref="HollandHogsSeason"/>, each sent against the
/// stream version it finds, so a test can tell a history without counting events.
/// </summary>
public sealed class TreasurersDesk(BallBankApi api, HollandHogsSeason hogs)
{
    private IDocumentStore Store => api.Services.GetRequiredService<IDocumentStore>();

    /// <summary>
    /// Sam's $75 of dues and one $25 assessment, paid in part: two confirmed payments and a rejected one
    /// (an overpayment, part of it refunded), one still waiting; Jacob's confirmed payment and a
    /// waiver. Two more members owe their $50 dues in full.
    /// </summary>
    public async Task MixedHistory()
    {
        await Assess([hogs.Sams], 25m, new DateOnly(2026, 11, 1), "Trophy fund");

        var first = await Attest(hogs.Sam, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1");
        await Confirm(hogs.Sams, first);
        var wrong = await Attest(hogs.Sam, hogs.Sams, 20m, PaymentRail.Zelle, "ZL-0");
        await Reject(hogs.Sams, wrong, "Nothing arrived");
        var second = await Attest(hogs.Sam, hogs.Sams, 40m, PaymentRail.Venmo, "VN-2");
        await Confirm(hogs.Sams, second);
        await Adjust(hogs.Sams, 10m, "Refund of the overpayment", refund: true);
        await Attest(hogs.Sam, hogs.Sams, 5m, PaymentRail.Cash, null);

        var jacobs = await Attest(hogs.Jacob, hogs.Jacobs, 30m, PaymentRail.Cash, null);
        await Confirm(hogs.Jacobs, jacobs);
        await Adjust(hogs.Jacobs, -5m, "Waived: hosted the draft");
    }

    public async Task Assess(Guid[] memberIds, decimal amount, DateOnly dueDate, string memo) =>
        (await Send(hogs.Jacob, $"/leagues/{hogs.LeagueId}/seasons/2026/assessments",
            new AssessmentRequest(Guid.NewGuid(), amount, dueDate, memo, memberIds))).StatusCode.ShouldBe(HttpStatusCode.OK);

    public async Task<Guid> Attest(string subject, Guid memberId, decimal amount, PaymentRail rail, string? reference)
    {
        var attestationId = Guid.NewGuid();
        (await Send(subject, $"/leagues/{hogs.LeagueId}/accounts/{hogs.AccountOf(memberId)}/attestations",
            new AttestPaymentRequest(attestationId, amount, rail, reference, await VersionOf(memberId)))).StatusCode.ShouldBe(HttpStatusCode.Created);
        return attestationId;
    }

    public async Task Confirm(Guid memberId, Guid attestationId) =>
        (await Send(hogs.Jacob, $"/leagues/{hogs.LeagueId}/accounts/{hogs.AccountOf(memberId)}/attestations/{attestationId}/confirmation",
            new ConfirmPaymentRequest(await VersionOf(memberId)))).StatusCode.ShouldBe(HttpStatusCode.OK);

    public async Task Reject(Guid memberId, Guid attestationId, string reason) =>
        (await Send(hogs.Jacob, $"/leagues/{hogs.LeagueId}/accounts/{hogs.AccountOf(memberId)}/attestations/{attestationId}/rejection",
            new RejectPaymentRequest(reason, await VersionOf(memberId)))).StatusCode.ShouldBe(HttpStatusCode.OK);

    public async Task Adjust(Guid memberId, decimal amount, string reason, bool refund = false) =>
        (await Send(hogs.Jacob, $"/leagues/{hogs.LeagueId}/accounts/{hogs.AccountOf(memberId)}/adjustments",
            new PostAdjustmentRequest(Guid.NewGuid(), amount, reason, await VersionOf(memberId), refund))).StatusCode.ShouldBe(HttpStatusCode.Created);

    /// <summary>Waits for the projection daemon to have applied every event so far.</summary>
    public Task Settled() => Store.WaitForNonStaleProjectionDataAsync(TimeSpan.FromSeconds(30));

    private async Task<int> VersionOf(Guid memberId)
    {
        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        return (int)(await session.Events.FetchStreamStateAsync(hogs.AccountOf(memberId))).ShouldNotBeNull().Version;
    }

    private Task<HttpResponseMessage> Send(string subject, string path, object body)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body, body.GetType()) };
        message.Headers.Add(Idempotency.Header, Guid.NewGuid().ToString());
        return api.CreateClientFor(subject).SendAsync(message);
    }
}

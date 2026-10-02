using System.Diagnostics;
using System.Net.Http.Json;
using BallBank.Api;
using BallBank.Api.Features.Notifications;
using BallBank.Api.Features.Treasury;
using BallBank.Domain.Treasury;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine.Tracking;

namespace BallBank.Integration.Tests.Http;

/// <summary>
/// Any question about one league is answerable by filtering on <c>tenant.id</c>, not just the ones about a request:
/// the work a message sets off, the projection daemon and the tick carry it too.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class TenantTelemetryTests(PostgresFixture postgres)
{
    private BallBankApi Api => postgres.Api;

    [Fact]
    public async Task A_notification_handler_carries_the_league_on_its_span_and_logs()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        await ConnectDiscord(hogs);
        using var traced = new TracedRequest();

        await Quiesced(async () =>
        {
            var response = await Api.CreateClientFor(hogs.Jacob).SendAsync(traced.Under(OpenSeason(hogs)));
            response.IsSuccessStatusCode.ShouldBeTrue();
        });

        var handlers = traced.MessageSpans.Where(span => span.OperationName == typeof(SendNotification).FullName).ToList();
        handlers.ShouldNotBeEmpty();
        handlers.ShouldAllBe(span => Equals(span.GetTagItem(TenantTelemetry.TenantId), hogs.LeagueId.ToString()));

        var statements = StatementsWhileHandling(handlers);
        statements.ShouldNotBeEmpty();
        statements.ShouldAllBe(log => log.IsInLeague(hogs.LeagueId));
    }

    [Fact]
    public async Task The_daemon_logs_what_it_applies_under_the_league_the_events_belong_to()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var desk = new TreasurersDesk(Api, hogs);

        var attestation = await desk.Attest(hogs.Sam, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1234");
        await desk.Confirm(hogs.Sams, attestation);
        await desk.Settled();

        // Other tests' leagues are applied by the same daemon, so ours are the ones that name it.
        var ours = Api.Logs.All
            .Where(log => log.Category == typeof(LeaguePotProjection).FullName && log.Message.Contains(hogs.LeagueId.ToString()))
            .ToList();
        ours.ShouldNotBeEmpty();
        ours.ShouldAllBe(log => log.IsInLeague(hogs.LeagueId));
    }

    // Wolverine's own lines around a handler (arriving, starting, finishing) are outside its scope, so Marten's statements stand for it.
    private List<CapturedLog> StatementsWhileHandling(IEnumerable<Activity> handlers)
    {
        var spans = handlers.Select(span => span.SpanId.ToString()).ToHashSet();
        return Api.Logs.All
            .Where(log => log.Category == typeof(IDocumentStore).FullName)
            .Where(log => log.Scope.GetValueOrDefault("SpanId") is string span && spans.Contains(span))
            .ToList();
    }

    private async Task ConnectDiscord(HollandHogsSeason hogs)
    {
        var webhook = Api.Discord.NewWebhook();
        var response = await Api.CreateClientFor(hogs.Jacob).PutAsJsonAsync(
            $"/leagues/{hogs.LeagueId}/notifications/discord",
            new DiscordSettingsRequest(webhook.Url, AnnouncePayments: false, PostDigest: false));
        response.IsSuccessStatusCode.ShouldBeTrue();
    }

    private static HttpRequestMessage OpenSeason(HollandHogsSeason hogs)
    {
        var open = new HttpRequestMessage(HttpMethod.Post, $"/leagues/{hogs.LeagueId}/seasons")
        {
            Content = JsonContent.Create(new OpenSeasonRequest("2027", 75m, new DateOnly(2027, 10, 1))),
        };
        open.Headers.Add(Idempotency.Header, Guid.NewGuid().ToString());
        return open;
    }

    private async Task<ITrackedSession> Quiesced(Func<Task> action) =>
        await Api.Services.GetRequiredService<IHost>()
            .TrackActivity()
            .Timeout(TimeSpan.FromSeconds(30))
            .DoNotAssertOnExceptionsDetected()
            .ExecuteAndWaitAsync(_ => action());
}

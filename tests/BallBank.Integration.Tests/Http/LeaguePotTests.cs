using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Text.Json;
using BallBank.Api;
using BallBank.Api.Features.Treasury;
using BallBank.Domain.Treasury;
using JasperFx.Events.Daemon;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace BallBank.Integration.Tests.Http;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class LeaguePotTests(PostgresFixture postgres)
{
    private BallBankApi Api => postgres.Api;
    private IDocumentStore Store => Api.Services.GetRequiredService<IDocumentStore>();

    [Fact]
    public async Task The_daemon_catches_the_pot_up_within_seconds_of_a_confirmation()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var desk = new TreasurersDesk(Api, hogs);

        var attestation = await desk.Attest(hogs.Sam, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1234");
        await desk.Confirm(hogs.Sams, attestation);

        // No waiting on the store: only the daemon running inside the API can have built this.
        var pot = await Eventually(async () => await PotOf(hogs) is { Confirmed: 50m } built ? built : null);

        pot.ShouldSatisfyAllConditions(
            p => p.Assessed.ShouldBe(200m),
            p => p.Confirmed.ShouldBe(50m),
            p => p.Pot.ShouldBe(50m),
            p => p.Outstanding.ShouldBe(150m),
            p => p.PendingAttestations.ShouldBe(0),
            p => p.Accounts.Count.ShouldBe(4));
    }

    [Fact]
    public async Task The_pot_of_a_mixed_history_is_what_the_events_add_up_to()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var desk = new TreasurersDesk(Api, hogs);
        await desk.MixedHistory();
        await desk.Settled();

        var pot = (await PotOf(hogs)).ShouldNotBeNull();

        pot.ShouldSatisfyAllConditions(
            p => p.Assessed.ShouldBe(225m),
            p => p.Confirmed.ShouldBe(120m),
            p => p.Refunded.ShouldBe(10m),
            p => p.Adjusted.ShouldBe(5m),
            p => p.Pot.ShouldBe(110m),
            p => p.Outstanding.ShouldBe(115m),
            p => p.Owed.ShouldBe(5m),
            p => p.PendingAttestations.ShouldBe(1));
        pot.Accounts.Single(a => a.MemberId == hogs.Sams).ShouldSatisfyAllConditions(
            a => a.Balance.ShouldBe(-5m),
            a => a.PendingAttestations.ShouldBe(1),
            a => a.EarliestDueDate.ShouldBe(HollandHogsSeason.DueDate));
        pot.Accounts.Single(a => a.MemberId == hogs.Jacobs).Balance.ShouldBe(15m);
    }

    [Fact]
    public async Task The_pot_records_the_last_event_it_applied()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var desk = new TreasurersDesk(Api, hogs);
        await desk.MixedHistory();
        await desk.Settled();

        var pot = (await PotOf(hogs)).ShouldNotBeNull();

        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        var last = (await session.Events.QueryAllRawEvents()
                .Where(e => e.StreamId == hogs.AccountOf(hogs.Jacobs) || e.StreamId == hogs.AccountOf(hogs.Sams))
                .OrderByDescending(e => e.Sequence)
                .ToListAsync())
            .First();
        pot.LastSequence.ShouldBe(last.Sequence);
        pot.AsOf.ShouldBe(last.Timestamp);
    }

    [Fact]
    public async Task A_rebuild_reproduces_the_same_document()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var desk = new TreasurersDesk(Api, hogs);
        await desk.MixedHistory();
        await desk.Settled();
        var before = JsonSerializer.Serialize((await PotOf(hogs)).ShouldNotBeNull());

        // The runbook's order: stop the daemon, rebuild, start it again.
        var daemon = Api.Services.GetRequiredService<IProjectionCoordinator>().DaemonForMainDatabase();
        await daemon.StopAllAsync();

        // A rebuild that did nothing would leave a document behind; this one has to make it again.
        await using (var session = Store.LightweightSession(hogs.LeagueId.ToString()))
        {
            session.Delete<LeaguePot>(SeasonIds.SeasonId(hogs.LeagueId, "2026"));
            await session.SaveChangesAsync();
        }

        (await PotOf(hogs)).ShouldBeNull();

        await daemon.RebuildProjectionAsync<LeaguePotProjection>(CancellationToken.None);
        await daemon.StartAllAsync();
        await desk.Settled();

        JsonSerializer.Serialize((await PotOf(hogs)).ShouldNotBeNull()).ShouldBe(before);
    }

    [Fact]
    public async Task The_lag_is_recorded_with_the_league_it_was_for()
    {
        var lags = new ConcurrentQueue<(double Seconds, string? Tenant)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == ProjectionLagMetric.MeterName && instrument.Name == ProjectionLagMetric.InstrumentName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
        {
            string? tenant = null;
            foreach (var tag in tags)
            {
                if (tag.Key == TenantTelemetry.TenantId)
                {
                    tenant = tag.Value?.ToString();
                }
            }

            lags.Enqueue((value, tenant));
        });
        listener.Start();

        var hogs = await HollandHogsSeason.Open(Api);
        var desk = new TreasurersDesk(Api, hogs);
        var attestation = await desk.Attest(hogs.Sam, hogs.Sams, 50m, PaymentRail.Venmo, "VN-1234");
        await desk.Confirm(hogs.Sams, attestation);
        await desk.Settled();

        var ours = lags.Where(l => l.Tenant == hogs.LeagueId.ToString()).ToList();
        ours.ShouldNotBeEmpty();
        ours.ShouldAllBe(l => l.Seconds < 60);
    }

    private async Task<LeaguePot?> PotOf(HollandHogsSeason hogs)
    {
        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        return await session.LoadAsync<LeaguePot>(SeasonIds.SeasonId(hogs.LeagueId, "2026"));
    }

    private static async Task<T> Eventually<T>(Func<Task<T?>> read) where T : class
    {
        var giveUp = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            if (await read() is { } value)
            {
                return value;
            }

            if (DateTime.UtcNow > giveUp)
            {
                throw new TimeoutException("The projection did not catch up.");
            }

            await Task.Delay(100);
        }
    }
}

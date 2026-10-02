using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using BallBank.Api;
using BallBank.Domain.Treasury;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace BallBank.Integration.Tests.Http;

/// <summary>
/// The local rows of docs/numbers.md: a season's worth of commands against the in-memory API over a throwaway
/// PostgreSQL, reading the lag the way the deployed API reports it (<c>ballbank.projection.lag</c>) and counting
/// the events the season wrote. Run it for the figures: <c>dotnet test tests/BallBank.Integration.Tests
/// --filter "FullyQualifiedName~SeasonMeasurementTests" --logger "console;verbosity=detailed"</c>.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class SeasonMeasurementTests(PostgresFixture postgres, ITestOutputHelper output)
{
    private const double LagBudgetSeconds = 10;

    private BallBankApi Api => postgres.Api;

    [Fact]
    public async Task A_season_of_commands_leaves_the_pot_within_the_lag_budget_and_counts_what_it_wrote()
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
            foreach (var tag in tags)
            {
                if (tag.Key == TenantTelemetry.TenantId)
                {
                    lags.Enqueue((value, tag.Value?.ToString()));
                }
            }
        });
        listener.Start();

        var hogs = await HollandHogsSeason.Open(Api);
        var desk = new TreasurersDesk(Api, hogs);
        await desk.MixedHistory();
        foreach (var (subject, memberId) in new[] { (hogs.Sam, hogs.Sams), (hogs.Jacob, hogs.Jacobs) })
        {
            for (var instalment = 1; instalment <= 5; instalment++)
            {
                var attestation = await desk.Attest(subject, memberId, 10m, PaymentRail.Venmo, $"VN-{instalment}");
                await desk.Confirm(memberId, attestation);
            }
        }

        await desk.Settled();

        var ours = lags.Where(l => l.Tenant == hogs.LeagueId.ToString()).Select(l => l.Seconds).Order().ToList();
        ours.ShouldNotBeEmpty();
        await using var session = Api.Services.GetRequiredService<IDocumentStore>().QuerySession(hogs.LeagueId.ToString());
        var events = await session.Events.QueryAllRawEvents().CountAsync();

        var p95 = Percentile(ours, 0.95);
        output.WriteLine($"MEASURED events={events} lag_samples={ours.Count} lag_p50_s={Percentile(ours, 0.50):F3} lag_p95_s={p95:F3} lag_max_s={ours[^1]:F3}");

        p95.ShouldBeLessThan(LagBudgetSeconds);
    }

    // Nearest rank: the smallest sample that at least this share of the samples do not exceed.
    private static double Percentile(IReadOnlyList<double> sorted, double share) =>
        sorted[(int)Math.Ceiling(share * sorted.Count) - 1];
}

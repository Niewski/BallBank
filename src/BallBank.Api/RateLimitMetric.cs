using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BallBank.Api;

/// <summary>
/// The requests refused for spending their budget (ADR-0013), as a counter tagged with the party that spent it
/// (<c>partition</c>: league, caller or webhook) and, for a league, the league (<c>tenant.id</c>) — so a dashboard
/// can show which league is running into its limit.
/// </summary>
public static class RateLimitMetric
{
    public const string MeterName = "BallBank.RateLimiting";
    public const string InstrumentName = "ballbank.ratelimit.rejections";
    public const string Partition = "partition";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Rejections = Meter.CreateCounter<long>(
        InstrumentName,
        unit: "{request}",
        description: "Requests refused with 429 for spending their budget.");

    public static void Rejected(string partition, string? tenantId)
    {
        var tags = new TagList { { Partition, partition } };
        if (tenantId is not null)
        {
            tags.Add(TenantTelemetry.TenantId, tenantId);
        }

        Rejections.Add(1, tags);
    }
}

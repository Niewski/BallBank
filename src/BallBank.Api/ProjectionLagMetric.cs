using System.Diagnostics.Metrics;

namespace BallBank.Api;

/// <summary>
/// How far an async projection trails the events it reads (ADR-0006): the seconds from an event being
/// recorded to the projection applying it, as a histogram tagged with the league the event belongs to
/// (<c>tenant.id</c>), the projection and the type of event.
/// </summary>
public static class ProjectionLagMetric
{
    public const string MeterName = "BallBank.Projections";
    public const string InstrumentName = "ballbank.projection.lag";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Histogram<double> Lag = Meter.CreateHistogram<double>(
        InstrumentName,
        unit: "s",
        description: "Time from an event being recorded to an async projection applying it.");

    public static void Record(string projection, string tenantId, string eventType, TimeSpan lag) =>
        Lag.Record(
            lag.TotalSeconds,
            new KeyValuePair<string, object?>(TenantTelemetry.TenantId, tenantId),
            new KeyValuePair<string, object?>("projection", projection),
            new KeyValuePair<string, object?>("event.type", eventType));
}

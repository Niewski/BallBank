using System.Diagnostics;

namespace BallBank.Api;

/// <summary>
/// Names the league on what is recorded for it, as <c>tenant.id</c>, so a problem can be traced to one league: on
/// the span and the logs of every request under <c>/leagues/{leagueId}</c> (<see cref="UseTenantTelemetry"/>), and,
/// as a log scope, on whatever a message handler, the tick or the projection daemon logs (<see cref="BeginTenantScope"/>).
/// It is the name Wolverine gives the same tag on the spans of the messages it handles.
/// What the host logs before routing has read the league from the path (the request starting, host
/// filtering, route matching) and the request-finished line carry the request's trace id but not the league.
/// </summary>
public static class TenantTelemetry
{
    public const string TenantId = "tenant.id";

    /// <summary>Opens a log scope carrying <c>tenant.id</c>: what is logged until it is disposed names the league.</summary>
    public static IDisposable? BeginTenantScope(this ILogger logger, string tenant) =>
        logger.BeginScope(new KeyValuePair<string, object>[] { new(TenantId, tenant) });

    /// <summary>Goes first, so authorization refusals and unhandled exceptions are tagged too.</summary>
    public static IApplicationBuilder UseTenantTelemetry(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (LeagueTenant.Of(context) is not { } tenant)
            {
                await next(context);
                return;
            }

            Activity.Current?.SetTag(TenantId, tenant);

            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(TenantTelemetry));
            using (logger.BeginTenantScope(tenant))
            {
                await next(context);
            }
        });
}

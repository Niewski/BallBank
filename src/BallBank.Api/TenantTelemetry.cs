using System.Diagnostics;

namespace BallBank.Api;

/// <summary>
/// Tags every request under <c>/leagues/{leagueId}</c> with its league, as <c>tenant.id</c> on the
/// request's span and on every log written while it is handled, so a problem can be traced to one
/// league. <c>tenant.id</c> is the name Wolverine gives the same tag on the endpoints it runs.
/// </summary>
public static class TenantTelemetry
{
    public const string TenantId = "tenant.id";

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
            using (logger.BeginScope(new KeyValuePair<string, object>[] { new(TenantId, tenant) }))
            {
                await next(context);
            }
        });
}

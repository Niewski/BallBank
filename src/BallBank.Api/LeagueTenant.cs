using Wolverine.Http.Runtime.MultiTenancy;

namespace BallBank.Api;

/// <summary>
/// The tenant of a request: the league named by the <c>leagueId</c> route argument (ADR-0004), in the
/// canonical form of its GUID, so the tenant never depends on how the client spelled it. Wolverine,
/// the telemetry and every session opened on a league agree on this one string.
/// </summary>
public sealed class LeagueTenant : ITenantDetection
{
    public const string RouteArgument = "leagueId";

    /// <summary>The league in the route, or <c>null</c> for a request outside any league.</summary>
    public static string? Of(HttpContext context) =>
        Guid.TryParse(context.GetRouteValue(RouteArgument) as string, out var leagueId) ? leagueId.ToString() : null;

    public ValueTask<string?> DetectTenant(HttpContext context) => ValueTask.FromResult(Of(context));
}

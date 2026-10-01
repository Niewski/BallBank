using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace BallBank.Api;

/// <summary>
/// A league's use of BallBank is never slowed by another league (ADR-0013): ASP.NET Core's rate limiter,
/// partitioned so each league has a token bucket of its own. In process, while there is one replica (ADR-0007).
/// A refused request is <c>429</c> as problem details, with <c>Retry-After</c>.
/// </summary>
public static class RateLimiting
{
    /// <summary>The problem type of a request refused for spending its budget.</summary>
    public const string LimitedType = "rate-limited";

    public static IServiceCollection AddRequestBudgets(this IServiceCollection services)
    {
        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<RateLimitOptions>>((options, limits) =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context => PartitionFor(context, limits.Value));
            options.OnRejected = (context, cancellation) => Refuse(context.HttpContext, context.Lease);
        });

        return services;
    }

    private static Party? PartyOf(HttpContext context) =>
        context.Request.Path.StartsWithSegments("/webhooks")
            ? new(Scope.Webhook, context.Connection.RemoteIpAddress?.ToString() ?? "unknown")
        : LeagueTenant.Of(context) is { } league ? new(Scope.League, league)
        : context.User.FindFirstValue("sub") is { } caller ? new(Scope.Caller, caller)
        : null;

    private static RateLimitPartition<string> PartitionFor(HttpContext context, RateLimitOptions limits)
    {
        if (PartyOf(context) is not { } party)
        {
            return RateLimitPartition.GetNoLimiter("unlimited");
        }

        var budget = party.Scope switch
        {
            Scope.League => limits.League,
            Scope.Caller => limits.Caller,
            _ => limits.Webhook,
        };

        return RateLimitPartition.GetTokenBucketLimiter($"{party.Name}:{party.Key}", _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = budget.Burst,
            TokensPerPeriod = budget.Refill,
            ReplenishmentPeriod = budget.Every,
            AutoReplenishment = true,
            QueueLimit = 0,
        });
    }

    private enum Scope
    {
        League,
        Caller,
        Webhook,
    }

    private readonly record struct Party(Scope Scope, string Key)
    {
        public string Name => Scope.ToString().ToLowerInvariant();
    }

    private static async ValueTask Refuse(HttpContext context, RateLimitLease lease)
    {
        if (PartyOf(context) is { } party)
        {
            RateLimitMetric.Rejected(party.Name, party.Scope == Scope.League ? party.Key : null);
        }

        var wait = lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? retryAfter : TimeSpan.Zero;
        var seconds = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds));
        context.Response.Headers[HeaderNames.RetryAfter] = seconds.ToString(CultureInfo.InvariantCulture);

        await context.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails =
            {
                Status = StatusCodes.Status429TooManyRequests,
                Type = LimitedType,
                Title = "Too many requests",
                Detail = $"Too many requests. Try again in {seconds} seconds.",
            },
        });
    }
}

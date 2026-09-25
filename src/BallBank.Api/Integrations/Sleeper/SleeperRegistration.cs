using System.Net.Http.Headers;
using System.Threading.RateLimiting;

namespace BallBank.Api.Integrations.Sleeper;

public static class SleeperRegistration
{
    /// <summary>
    /// The typed <see cref="SleeperClient"/>. ServiceDefaults adds the standard resilience handler to
    /// every client; this adds Sleeper's address, a user agent naming BallBank, and a rate limit.
    /// </summary>
    public static IServiceCollection AddSleeper(this IServiceCollection services)
    {
        var version = typeof(SleeperClient).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        services.AddHttpClient<SleeperClient>(http =>
            {
                http.BaseAddress = SleeperClient.BaseAddress;
                http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BallBank", version));
                http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(+https://github.com/Niewski/BallBank)"));
            })
            .AddHttpMessageHandler(() => new SleeperRateLimit());

        return services;
    }

    /// <summary>
    /// Sleeper asks callers to stay under roughly 1000 calls a minute per IP. This process makes at most
    /// 10 a second (600 a minute), retries included, and queues the rest.
    /// </summary>
    private sealed class SleeperRateLimit : DelegatingHandler
    {
        // One bucket for the whole process: handlers are rotated, the limit must not be.
        private static readonly TokenBucketRateLimiter Limiter = new(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 10,
            TokensPerPeriod = 10,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = 1000,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            using var lease = await Limiter.AcquireAsync(1, cancellation);
            if (!lease.IsAcquired)
            {
                throw new HttpRequestException("Too many calls to Sleeper are already waiting.");
            }

            return await base.SendAsync(request, cancellation);
        }
    }
}

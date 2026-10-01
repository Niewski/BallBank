using System.Threading.RateLimiting;
using BallBank.Api.Features.Notifications;

namespace BallBank.Api.Integrations.Twilio;

public static class TwilioRegistration
{
    public static readonly Uri BaseAddress = new("https://api.twilio.com/");

    public static IServiceCollection AddTwilio(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioOptions>(configuration.GetSection(TwilioOptions.Section));

        // The client's own logging names the request's address, which holds the account SID: left out, as Discord's is.
        services.AddHttpClient<TwilioSmsChannel>(http => http.BaseAddress = BaseAddress)
            .RemoveAllLoggers()
            .AddHttpMessageHandler(() => new TwilioRateLimit());
        services.AddTransient<INotificationChannel>(services => services.GetRequiredService<TwilioSmsChannel>());

        return services;
    }

    /// <summary>A toll-free number sends 3 messages a second: at most that per process, retries included; the rest queue.</summary>
    private sealed class TwilioRateLimit : DelegatingHandler
    {
        // One bucket for the whole process: handlers are rotated, the limit must not be.
        private static readonly TokenBucketRateLimiter Limiter = new(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 3,
            TokensPerPeriod = 3,
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
                throw new HttpRequestException("Too many calls to Twilio are already waiting.");
            }

            return await base.SendAsync(request, cancellation);
        }
    }
}

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using BallBank.Api.Integrations.Discord;
using BallBank.Api.Integrations.Sleeper;
using BallBank.Domain;
using BallBank.Integration.Tests.Discord;
using BallBank.Integration.Tests.Sleeper;
using JasperFx.CommandLine;
using Marten;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace BallBank.Integration.Tests.Http;

/// <summary>
/// The API hosted in memory over the test database. Stands in for Auth0 with a signing key generated
/// when the factory is created, so tokens are real JWTs checked by the real bearer handler and no
/// Auth0 tenant is needed. The request budgets are raised, so tests running in parallel never spend one;
/// a factory made with <see cref="RateLimitSettings.Lowered"/> keeps them small enough to spend in a test.
/// </summary>
public sealed class BallBankApi(string connectionString, IReadOnlyDictionary<string, string?>? limits = null)
    : WebApplicationFactory<Program>
{
    public const string Domain = "ballbank-test.invalid";
    public const string Issuer = $"https://{Domain}/";
    public const string Audience = "https://api.ballbank.test";

    /// <summary>A route that throws a <see cref="DomainException"/>, for testing how refusals are reported.</summary>
    public const string RefusingPath = "/test/refuse";
    public const string RefusalMessage = "That member is already claimed.";

    /// <summary>A route under <c>/webhooks</c> that answers <c>204</c>, for testing how webhooks are budgeted.</summary>
    public const string WebhookPath = "/webhooks/test";

    /// <summary>The one origin the test host lets the web app call it from.</summary>
    public const string WebOrigin = "https://web.ballbank.test";

    /// <summary>Stands in for the address a request came from: a test host has no network to read it from.</summary>
    public const string RemoteAddressHeader = "X-Test-Remote-Address";

    // Program hands over to the JasperFx commands, which only start the host it built when told to;
    // a factory that never sees the host start has no server to serve requests.
    static BallBankApi() => JasperFxEnvironment.AutoStartHost = true;

    private readonly SecurityKey _signingKey = NewSigningKey();

    /// <summary>What the API's Sleeper client talks to instead of Sleeper.</summary>
    public FakeSleeper Sleeper { get; } = new();

    /// <summary>What the API's Discord channel talks to instead of Discord.</summary>
    public FakeDiscord Discord { get; } = new();

    /// <summary>Makes the commit of a league's notifications fail, for a test that needs to see them roll back.</summary>
    public NotificationCommitFailure NotificationCommits { get; } = new();

    /// <summary>Every log entry the API writes, whatever the configured log levels.</summary>
    public CapturedLogs Logs { get; } = new();

    /// <summary>The API's clock: the wall clock until a test sets it.</summary>
    public TestClock Clock { get; } = new();

    public HttpClient CreateClientFor(string subject)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenFor(subject));
        return client;
    }

    public string TokenFor(
        string? subject,
        string issuer = Issuer,
        string audience = Audience,
        SecurityKey? signingKey = null,
        DateTime? expires = null)
    {
        var expiry = expires ?? DateTime.UtcNow.AddHours(1);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(subject is null ? [] : [new Claim("sub", subject)]),
            NotBefore = expiry.AddHours(-2),
            Expires = expiry,
            SigningCredentials = new SigningCredentials(signingKey ?? _signingKey, SecurityAlgorithms.RsaSha256),
        });
    }

    public static SecurityKey NewSigningKey() =>
        new RsaSecurityKey(RSA.Create(2048)) { KeyId = Guid.NewGuid().ToString("N") };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:ballbank", connectionString);
        builder.UseSetting("Auth0:Domain", Domain);
        builder.UseSetting("Auth0:Audience", Audience);
        builder.UseSetting("Cors:AllowedOrigins:0", WebOrigin);

        // Webhooks are only accepted on Discord's own hosts; the fake's is not one of them, so say so.
        builder.UseSetting("Notifications:DiscordHosts:0", FakeDiscord.Host);

        // A send that keeps failing is retried a few times and then dead-lettered: in milliseconds, not minutes.
        builder.UseSetting("Notifications:RetryDelays:0", "00:00:00.050");
        builder.UseSetting("Notifications:RetryDelays:1", "00:00:00.050");

        foreach (var (key, value) in limits ?? RateLimitSettings.Raised)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureLogging(logging => logging
            .AddProvider(Logs)
            .AddFilter<CapturedLogs>(category: null, LogLevel.Trace));

        builder.ConfigureTestServices(services =>
        {
            // The discovery document Auth0 would serve, minus the HTTP call.
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                options.Configuration = new OpenIdConnectConfiguration
                {
                    Issuer = Issuer,
                    SigningKeys = { _signingKey },
                });

            // Tokens are minted and checked against the wall clock, so moving this one leaves sign-in alone.
            services.AddSingleton<TimeProvider>(Clock);

            services.ConfigureMarten(options => options.Listeners.Add(NotificationCommits));

            services.AddHttpClient<SleeperClient>().ConfigurePrimaryHttpMessageHandler(Sleeper.Handler);
            services.AddHttpClient<DiscordWebhookChannel>().ConfigurePrimaryHttpMessageHandler(Discord.Handler);

            // The standard resilience handler as configured, but giving up on a failing Sleeper in
            // seconds rather than half a minute. One API serves every test, so the circuit breaker
            // never opens: the tests that make Sleeper fail on purpose would otherwise break the
            // circuit for whichever tests call Sleeper next.
            services.ConfigureAll<HttpStandardResilienceOptions>(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromMilliseconds(500);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(2);
                options.Retry.Delay = TimeSpan.Zero;
                options.CircuitBreaker.MinimumThroughput = int.MaxValue;
            });

            services.AddSingleton<IStartupFilter>(new TestPipeline());
        });
    }

    /// <summary>
    /// Reads the address a request came from out of <see cref="RemoteAddressHeader"/> ahead of the API's own
    /// pipeline, and appends <see cref="RefusingPath"/> and <see cref="WebhookPath"/> behind it, so the API's
    /// error handling and request budgets apply to them.
    /// </summary>
    private sealed class TestPipeline : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, forward) =>
            {
                if (IPAddress.TryParse(context.Request.Headers[RemoteAddressHeader], out var address))
                {
                    context.Connection.RemoteIpAddress = address;
                }

                return forward(context);
            });

            next(app);

            app.Map(RefusingPath, refusing => refusing.Run(_ => throw new DomainException(RefusalMessage)));
            app.Map(WebhookPath, webhook => webhook.Run(context =>
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            }));
        };
    }
}

/// <summary>The settings that give the API its request budgets (<c>RateLimits:…</c>), as a test host sets them.</summary>
public static class RateLimitSettings
{
    private static readonly string[] Scopes = ["League", "Caller", "Webhook"];

    /// <summary>Far more than any test spends.</summary>
    public static readonly IReadOnlyDictionary<string, string?> Raised = Of(burst: 1_000_000, refill: 1_000_000, TimeSpan.FromSeconds(1));

    /// <summary>
    /// A budget of <paramref name="burst"/> requests for every league, caller and address, which does not come back
    /// within a test: one token an hour.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> Lowered(int burst) => Of(burst, refill: 1, TimeSpan.FromHours(1));

    private static Dictionary<string, string?> Of(int burst, int refill, TimeSpan every)
    {
        var settings = new Dictionary<string, string?>();
        foreach (var scope in Scopes)
        {
            settings[$"RateLimits:{scope}:Burst"] = burst.ToString(CultureInfo.InvariantCulture);
            settings[$"RateLimits:{scope}:Refill"] = refill.ToString(CultureInfo.InvariantCulture);
            settings[$"RateLimits:{scope}:Every"] = every.ToString("c", CultureInfo.InvariantCulture);
        }

        return settings;
    }
}

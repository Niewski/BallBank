using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using BallBank.Api.Integrations.Sleeper;
using BallBank.Domain;
using BallBank.Integration.Tests.Sleeper;
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
/// Auth0 tenant is needed.
/// </summary>
public sealed class BallBankApi(string connectionString) : WebApplicationFactory<Program>
{
    public const string Domain = "ballbank-test.invalid";
    public const string Issuer = $"https://{Domain}/";
    public const string Audience = "https://api.ballbank.test";

    /// <summary>A route that throws a <see cref="DomainException"/>, for testing how refusals are reported.</summary>
    public const string RefusingPath = "/test/refuse";
    public const string RefusalMessage = "That member is already claimed.";

    private readonly SecurityKey _signingKey = NewSigningKey();

    /// <summary>What the API's Sleeper client talks to instead of Sleeper.</summary>
    public FakeSleeper Sleeper { get; } = new();

    /// <summary>Every log entry the API writes, whatever the configured log levels.</summary>
    public CapturedLogs Logs { get; } = new();

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

            services.AddHttpClient<SleeperClient>().ConfigurePrimaryHttpMessageHandler(Sleeper.Handler);

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

            services.AddSingleton<IStartupFilter>(new RefusingRoute());
        });
    }

    /// <summary>Appends <see cref="RefusingPath"/> behind the API's own pipeline, so the API's error handling applies.</summary>
    private sealed class RefusingRoute : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Map(RefusingPath, refusing => refusing.Run(_ => throw new DomainException(RefusalMessage)));
        };
    }
}

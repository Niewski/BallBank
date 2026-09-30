using System.Net.Http.Headers;
using BallBank.Integration.Tests.Discord;
using BallBank.Integration.Tests.Http;
using BallBank.Integration.Tests.Sleeper;
using Marten;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Wolverine.Runtime;

namespace BallBank.Specs.Support;

/// <summary>
/// The API hosted in memory over a throwaway PostgreSQL, started by the first scenario that needs it
/// and shared by every scenario after, each in a league of its own. Signs tokens with the integration
/// tests' test issuer and talks to their fake Sleeper.
/// </summary>
public sealed class SpecsHost
{
    private static readonly SemaphoreSlim Starting = new(1, 1);
    private static SpecsHost? _shared;

    private readonly PostgreSqlContainer _database;
    private readonly BallBankApi _api;
    private readonly WebApplicationFactory<Program> _host;

    private SpecsHost(PostgreSqlContainer database)
    {
        _database = database;
        _api = new BallBankApi(database.GetConnectionString());
        _host = _api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureMarten(options => options.Listeners.Add(Commits))));
    }

    public FakeSleeper Sleeper => _api.Sleeper;

    public FakeDiscord Discord => _api.Discord;

    /// <summary>
    /// Waits until the API has nothing left waiting in its inbox: the events its stores forwarded, and the
    /// messages those caused, are handled. Notifications are delivered after the command that caused them
    /// has been answered, so a spec about what was said reads only after this.
    /// </summary>
    public async Task Delivered()
    {
        var runtime = _host.Services.GetRequiredService<IWolverineRuntime>();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var counts = await runtime.Storage.Admin.FetchCountsAsync();
            if (counts.Incoming + counts.Scheduled == 0)
            {
                return;
            }

            await Task.Delay(25);
        }
    }

    /// <summary>
    /// The API's clock. Features run in parallel against this one API, so a scenario that sets it
    /// moves "now" for every scenario running beside it.
    /// </summary>
    public TestClock Clock => _api.Clock;

    /// <summary>Holds commits back when a scenario needs two requests to race.</summary>
    public CommitGate Commits { get; } = new();

    public IDocumentStore Store => _host.Services.GetRequiredService<IDocumentStore>();

    public static async Task<SpecsHost> Shared()
    {
        await Starting.WaitAsync();
        try
        {
            if (_shared is null)
            {
                var database = new PostgreSqlBuilder("postgres:17-alpine").Build();
                try
                {
                    await database.StartAsync();
                    var host = new SpecsHost(database);

                    // Started here, once: the factory starts its server on first use, and features run in parallel.
                    _ = host._host.Server;
                    _shared = host;
                }
                catch
                {
                    await database.DisposeAsync();
                    throw;
                }
            }

            return _shared;
        }
        finally
        {
            Starting.Release();
        }
    }

    public static async Task StopAsync()
    {
        if (_shared is { } host)
        {
            _shared = null;
            await host._api.DisposeAsync(); // disposes the host derived from it too
            await host._database.DisposeAsync();
        }
    }

    /// <summary>A client signed in as <paramref name="subject"/>.</summary>
    public HttpClient ClientFor(string subject)
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _api.TokenFor(subject));
        return client;
    }
}

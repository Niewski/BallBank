using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;

namespace BallBank.Integration.Tests.Sleeper;

/// <summary>
/// Sleeper faked at the message-handler level. Serves the synthetic Holland Hogs fixtures
/// (<c>Sleeper/Fixtures</c>), answers <c>404</c> to anything it was not told about, and records every
/// request, so no call ever reaches the real Sleeper. Tests add their own routes for the cases they need.
/// </summary>
public sealed class FakeSleeper
{
    public const string HollandHogsLeagueId = "900000000000000001";
    public const string JacobUserId = "100000000000000001";

    private readonly ConcurrentDictionary<string, Func<CancellationToken, Task<HttpResponseMessage>>> _routes = new();

    public FakeSleeper()
    {
        Serve("/state/nfl", "state-nfl.json");
        Serve("/user/jacob", "user-jacob.json");
        Serve($"/user/{JacobUserId}/leagues/nfl/2026", "user-jacob-leagues-nfl-2026.json");
        Serve($"/league/{HollandHogsLeagueId}", "league.json");
        Serve($"/league/{HollandHogsLeagueId}/users", "league-users.json");
        Serve($"/league/{HollandHogsLeagueId}/rosters", "league-rosters.json");
    }

    public ConcurrentQueue<HttpRequestMessage> Requests { get; } = new();

    /// <summary>Answers <paramref name="path"/> (relative to <c>/v1</c>) with a fixture file.</summary>
    public void Serve(string path, string fixture) =>
        ServeJson(path, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Sleeper", "Fixtures", fixture)));

    public void ServeJson(string path, string json) =>
        _routes[path] = _ => Task.FromResult(Json(HttpStatusCode.OK, json));

    public void Fails(string path, HttpStatusCode status) =>
        _routes[path] = _ => Task.FromResult(Json(status, """{"message":"Something went wrong"}"""));

    /// <summary>The connection to Sleeper fails, as it does when Sleeper is down or DNS is broken.</summary>
    public void Unreachable(string path) =>
        _routes[path] = _ => throw new HttpRequestException("No connection could be made to api.sleeper.app.");

    /// <summary>Sleeper accepts the call and never answers.</summary>
    public void Hangs(string path) =>
        _routes[path] = async cancellation =>
        {
            await Task.Delay(Timeout.Infinite, cancellation);
            throw new UnreachableException();
        };

    /// <summary>A fresh handler over this fake; the HTTP client factory disposes handlers as it rotates them.</summary>
    public HttpMessageHandler Handler() => new SleeperHandler(this);

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class SleeperHandler(FakeSleeper sleeper) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            sleeper.Requests.Enqueue(request);

            var uri = request.RequestUri!;
            if (uri.Host != "api.sleeper.app" || !uri.AbsolutePath.StartsWith("/v1/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"The fake Sleeper was asked for {uri}, which is not Sleeper's API.");
            }

            var path = uri.AbsolutePath["/v1".Length..];
            return sleeper._routes.TryGetValue(path, out var respond)
                ? respond(cancellation)
                : Task.FromResult(Json(HttpStatusCode.NotFound, """{"message":"Not found"}"""));
        }
    }
}

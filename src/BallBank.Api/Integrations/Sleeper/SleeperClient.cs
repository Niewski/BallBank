using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Polly;

namespace BallBank.Api.Integrations.Sleeper;

/// <summary>
/// Sleeper's public, unauthenticated read API (https://docs.sleeper.com), typed. Rides the resilient
/// <see cref="HttpClient"/> from ServiceDefaults, so timeouts and retries come from there; see
/// <see cref="SleeperRegistration"/> for the user agent and the rate limit. Any failure to get an answer
/// surfaces as <see cref="SleeperUnavailableException"/>.
/// </summary>
public sealed class SleeperClient(HttpClient http)
{
    public static readonly Uri BaseAddress = new("https://api.sleeper.app/v1/");

    // Snake_case on the wire; unknown fields are ignored, so Sleeper can add fields freely.
    private static readonly JsonSerializerOptions SnakeCase = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    // Named after the assembly, which is the source ServiceDefaults subscribes to.
    private static readonly ActivitySource Tracing = new(typeof(SleeperClient).Assembly.GetName().Name!);

    /// <summary>The user with this username, or <c>null</c> when Sleeper has no such user.</summary>
    public Task<SleeperUser?> FindUserAsync(string username, CancellationToken cancellation) =>
        GetAsync<SleeperUser>("user", $"user/{Uri.EscapeDataString(username)}", leagueId: null, cancellation);

    /// <summary>
    /// The season Sleeper's leagues are in now, from <c>/state/nfl</c>. That is <c>league_season</c>
    /// rather than <c>season</c>: after the Super Bowl Sleeper rolls leagues over to next year before
    /// the NFL season itself turns, and a treasurer then collects dues for the new one.
    /// </summary>
    public async Task<string> CurrentSeasonAsync(CancellationToken cancellation)
    {
        var state = await GetAsync<SleeperNflState>("state", "state/nfl", leagueId: null, cancellation)
            ?? throw new SleeperUnavailableException("Sleeper returned no NFL state.");

        return state.LeagueSeason ?? state.Season;
    }

    /// <summary>The NFL leagues a Sleeper user is in for one season.</summary>
    public async Task<IReadOnlyList<SleeperLeague>> GetLeaguesAsync(
        string userId, string season, CancellationToken cancellation) =>
        await GetAsync<SleeperLeague[]>(
            "user leagues",
            $"user/{Uri.EscapeDataString(userId)}/leagues/nfl/{Uri.EscapeDataString(season)}",
            leagueId: null,
            cancellation)
        ?? [];

    /// <summary>The league with this id, or <c>null</c> when Sleeper has no such league.</summary>
    public Task<SleeperLeague?> GetLeagueAsync(string leagueId, CancellationToken cancellation) =>
        GetAsync<SleeperLeague>("league", $"league/{Uri.EscapeDataString(leagueId)}", leagueId, cancellation);

    /// <summary>The users in a league, each with their team name and whether they are a commissioner.</summary>
    public async Task<IReadOnlyList<SleeperLeagueUser>> GetLeagueUsersAsync(string leagueId, CancellationToken cancellation) =>
        await GetAsync<SleeperLeagueUser[]>(
            "league users", $"league/{Uri.EscapeDataString(leagueId)}/users", leagueId, cancellation)
        ?? [];

    /// <summary>The rosters in a league, each with its owner if it has one.</summary>
    public async Task<IReadOnlyList<SleeperRoster>> GetLeagueRostersAsync(string leagueId, CancellationToken cancellation) =>
        await GetAsync<SleeperRoster[]>(
            "league rosters", $"league/{Uri.EscapeDataString(leagueId)}/rosters", leagueId, cancellation)
        ?? [];

    /// <summary>
    /// A league with its users and rosters, as read and as Sleeper sent them, for an import to decide
    /// from and keep. <c>null</c> when Sleeper has no such league.
    /// </summary>
    public async Task<SleeperLeagueResponses?> GetLeagueResponsesAsync(string leagueId, CancellationToken cancellation)
    {
        var escaped = Uri.EscapeDataString(leagueId);
        var league = await FetchAsync<SleeperLeague>("league", $"league/{escaped}", leagueId, cancellation);
        if (league?.Value is null)
        {
            return null;
        }

        var users = await FetchAsync<SleeperLeagueUser[]>("league users", $"league/{escaped}/users", leagueId, cancellation);
        var rosters = await FetchAsync<SleeperRoster[]>("league rosters", $"league/{escaped}/rosters", leagueId, cancellation);

        return new SleeperLeagueResponses(
            league.Value,
            users?.Value ?? [],
            rosters?.Value ?? [],
            league.Json,
            users?.Json ?? "[]",
            rosters?.Json ?? "[]");
    }

    private async Task<T?> GetAsync<T>(string operation, string path, string? leagueId, CancellationToken cancellation)
        where T : class =>
        (await FetchAsync<T>(operation, path, leagueId, cancellation))?.Value;

    /// <summary>The response read as <typeparamref name="T"/> with the JSON it was read from; <c>null</c> for a 404.</summary>
    private async Task<Fetched<T>?> FetchAsync<T>(string operation, string path, string? leagueId, CancellationToken cancellation)
        where T : class
    {
        using var activity = Tracing.StartActivity($"Sleeper {operation}");
        activity?.SetTag("sleeper.league.id", leagueId);

        try
        {
            using var response = await http.GetAsync(path, cancellation);

            // Sleeper answers an unknown user or league with null, but a 404 means the same thing.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellation);
            return new Fetched<T>(JsonSerializer.Deserialize<T>(json, SnakeCase), json);
        }
        catch (Exception failure) when (failure is HttpRequestException or ExecutionRejectedException or JsonException
            || (failure is OperationCanceledException && !cancellation.IsCancellationRequested))
        {
            activity?.SetStatus(ActivityStatusCode.Error, failure.Message);
            throw new SleeperUnavailableException($"Sleeper did not answer {path}.", failure);
        }
    }

    private sealed record Fetched<T>(T? Value, string Json);
}

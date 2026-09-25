namespace BallBank.Api.Integrations.Sleeper;

// The parts of Sleeper's responses BallBank reads. Property names map to Sleeper's snake_case fields.

/// <summary><c>/user/{username}</c></summary>
public sealed record SleeperUser(string UserId, string Username, string? DisplayName);

/// <summary><c>/state/nfl</c></summary>
public sealed record SleeperNflState(string Season, string? LeagueSeason);

/// <summary><c>/league/{id}</c>, and each entry of <c>/user/{id}/leagues/nfl/{season}</c>.</summary>
public sealed record SleeperLeague(string LeagueId, string Name, string Season, int TotalRosters);

/// <summary>Each entry of <c>/league/{id}/users</c>.</summary>
public sealed record SleeperLeagueUser(string UserId, string? DisplayName, bool? IsOwner, SleeperLeagueUserMetadata? Metadata)
{
    /// <summary>Sleeper's <c>is_owner</c>: a commissioner of the league. Absent or null for everyone else.</summary>
    public bool IsCommissioner => IsOwner == true;

    /// <summary>The name this user gave their team in this league, if they gave one.</summary>
    public string? TeamName => Metadata?.TeamName;
}

public sealed record SleeperLeagueUserMetadata(string? TeamName);

/// <summary>Each entry of <c>/league/{id}/rosters</c>. <see cref="OwnerId"/> is null for a roster nobody owns.</summary>
public sealed record SleeperRoster(int RosterId, string? OwnerId);

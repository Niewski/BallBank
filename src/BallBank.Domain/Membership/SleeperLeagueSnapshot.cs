namespace BallBank.Domain.Membership;

/// <summary>
/// What Sleeper said about one league at the moment it was imported, reduced to what the import
/// decides from. The API turns Sleeper's JSON into this; the domain never sees JSON or usernames, only ids.
/// </summary>
public sealed record SleeperLeagueSnapshot(
    string SleeperLeagueId,
    string Name,
    string Season,
    IReadOnlyList<SleeperLeagueSnapshot.User> Users,
    IReadOnlyList<SleeperLeagueSnapshot.Roster> Rosters)
{
    /// <summary>The roster this Sleeper user owns, or <c>null</c> when they own none (co-owning one does not count).</summary>
    public Roster? RosterOwnedBy(string sleeperUserId) =>
        Rosters.FirstOrDefault(r => r.OwnerUserId == sleeperUserId);

    public User? UserWithId(string? sleeperUserId) =>
        sleeperUserId is null ? null : Users.FirstOrDefault(u => u.UserId == sleeperUserId);

    /// <summary>A person in the Sleeper league. <see cref="IsCommissioner"/> is Sleeper's <c>is_owner</c>.</summary>
    public sealed record User(string UserId, string? DisplayName, bool IsCommissioner);

    /// <summary>A team in the Sleeper league, with its owner if it has one.</summary>
    public sealed record Roster(int RosterId, string? OwnerUserId, string? TeamName);
}

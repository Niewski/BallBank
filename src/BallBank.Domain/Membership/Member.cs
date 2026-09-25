namespace BallBank.Domain.Membership;

/// <summary>
/// The one person responsible for a team's dues in a league, and who (if anyone) holds it.
/// </summary>
/// <param name="HeldBy">The sign-in subject of the identity that claimed this member; <c>null</c> while unclaimed.</param>
public sealed record Member(
    Guid MemberId,
    int SleeperRosterId,
    string? SleeperUserId,
    string TeamName,
    string? SleeperDisplayName,
    bool SuggestedTreasurer,
    string? HeldBy = null,
    string? HolderDisplayName = null,
    bool IsTreasurer = false)
{
    public bool IsClaimed => HeldBy is not null;
}

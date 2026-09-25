using BallBank.Domain.Membership;

namespace BallBank.Api.Features.Membership;

/// <summary>
/// The leagues one identity is a member of, keyed by sign-in subject. One of the two cross-tenant
/// documents (ADR-0011): it lives in the default tenant so "my leagues" and the membership gate need
/// no cross-tenant scan. Derived from league events and written in the same transaction as them.
/// </summary>
public sealed class UserMemberships
{
    /// <summary>The sign-in subject (the token's <c>sub</c> claim).</summary>
    public string Id { get; set; } = "";

    public List<LeagueMembership> Leagues { get; set; } = [];
}

/// <summary>The member an identity holds in one league, with what "my leagues" shows about it.</summary>
public sealed record LeagueMembership(
    Guid LeagueId,
    string LeagueName,
    string Season,
    Guid MemberId,
    IReadOnlyList<string> Roles);

public static class Roles
{
    public const string Treasurer = "Treasurer";

    /// <summary>The roles a member holds, as the API names them.</summary>
    public static IReadOnlyList<string> Of(Member member) => member.IsTreasurer ? [Treasurer] : [];
}

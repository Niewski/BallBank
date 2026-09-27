using System.Security.Claims;
using BallBank.Api.Features.Membership;
using BallBank.Domain.Membership;
using Marten;

namespace BallBank.Api.Features.Treasury;

/// <summary>Who may read an account: a treasurer of its league, or its own member.</summary>
public static class AccountReaders
{
    /// <summary>
    /// The caller's membership of the league when it lets them read the account of <paramref name="memberId"/>,
    /// else <c>null</c>. The policy knows the caller is a member; whose account this is only the account knows.
    /// </summary>
    public static async Task<LeagueMembership?> CallerAsync(
        IDocumentStore store, ClaimsPrincipal user, Guid leagueId, Guid memberId, CancellationToken cancellation)
    {
        // UserMemberships lives in the default tenant, not the league's (ADR-0011).
        await using var memberships = store.QuerySession();
        var caller = (await memberships.LoadAsync<UserMemberships>(user.Subject(), cancellation))?
            .Leagues.FirstOrDefault(l => l.LeagueId == leagueId);
        return caller is not null && (caller.Roles.Contains(Roles.Treasurer) || caller.MemberId == memberId) ? caller : null;
    }

    /// <summary>The refusal for a caller <see cref="CallerAsync"/> turned away from the account's <paramref name="what"/>.</summary>
    public static IResult NotYourAccount(string what) => Results.Problem(
        statusCode: StatusCodes.Status403Forbidden,
        title: "Not your account",
        detail: $"Only a treasurer, or the account's own member, can read its {what}.");
}

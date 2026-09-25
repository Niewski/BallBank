using Marten;
using Microsoft.AspNetCore.Authorization;

namespace BallBank.Api.Features.Membership;

public static class Policies
{
    /// <summary>The caller is a member of the league in the route.</summary>
    public const string LeagueMember = "LeagueMember";

    /// <summary>The caller is a treasurer of the league in the route.</summary>
    public const string LeagueTreasurer = "LeagueTreasurer";

    /// <summary>The caller is a treasurer of the league in the route, or holds the member in the route.</summary>
    public const string LeagueTreasurerOrOwnMember = "LeagueTreasurerOrOwnMember";

    public static IServiceCollection AddLeaguePolicies(this IServiceCollection services)
    {
        services.AddScoped<IAuthorizationHandler, LeagueMembershipHandler>();
        return services.AddAuthorization(options =>
        {
            options.AddPolicy(LeagueMember, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new LeagueMemberRequirement()));
            options.AddPolicy(LeagueTreasurer, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new LeagueTreasurerRequirement()));
            options.AddPolicy(LeagueTreasurerOrOwnMember, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new LeagueTreasurerOrOwnMemberRequirement()));
        });
    }
}

/// <summary>A requirement met by what the caller's <see cref="UserMemberships"/> entry for the league in the route says.</summary>
public interface ILeagueRequirement : IAuthorizationRequirement
{
    bool IsMetBy(LeagueMembership membership, HttpContext request);
}

/// <summary>The caller's <see cref="UserMemberships"/> lists the league named by the <c>leagueId</c> route argument.</summary>
public sealed class LeagueMemberRequirement : ILeagueRequirement
{
    public bool IsMetBy(LeagueMembership membership, HttpContext request) => true;
}

/// <summary>The caller's membership of the league in the route carries the Treasurer role.</summary>
public sealed class LeagueTreasurerRequirement : ILeagueRequirement
{
    public bool IsMetBy(LeagueMembership membership, HttpContext request) => membership.Roles.Contains(Roles.Treasurer);
}

/// <summary>
/// The caller's membership of the league in the route carries the Treasurer role, or is of the member
/// named by the <c>memberId</c> route argument.
/// </summary>
public sealed class LeagueTreasurerOrOwnMemberRequirement : ILeagueRequirement
{
    private const string RouteArgument = "memberId";

    public bool IsMetBy(LeagueMembership membership, HttpContext request) =>
        membership.Roles.Contains(Roles.Treasurer)
        || (Guid.TryParse(request.GetRouteValue(RouteArgument) as string, out var memberId) && memberId == membership.MemberId);
}

/// <summary>
/// The fast gate in front of every league's pages (ADR-0011). It reads only the caller's memberships,
/// so a league the caller is not in is refused the same way whether it exists or not, and a guessed
/// league id reveals nothing. The memberships are read once, whatever the policy asks of them.
/// </summary>
public sealed class LeagueMembershipHandler(IDocumentStore store) : IAuthorizationHandler
{
    public async Task HandleAsync(AuthorizationHandlerContext context)
    {
        var pending = context.PendingRequirements.OfType<ILeagueRequirement>().ToList();
        if (pending.Count == 0
            || context.User.Identity?.IsAuthenticated != true
            || context.Resource is not HttpContext http
            || LeagueTenant.Of(http) is not { } league)
        {
            return;
        }

        // UserMemberships lives in the default tenant, not the league's.
        await using var session = store.QuerySession();
        var memberships = await session.LoadAsync<UserMemberships>(context.User.Subject(), http.RequestAborted);

        if (memberships?.Leagues.FirstOrDefault(l => l.LeagueId.ToString() == league) is not { } membership)
        {
            return;
        }

        foreach (var requirement in pending.Where(r => r.IsMetBy(membership, http)))
        {
            context.Succeed(requirement);
        }
    }
}

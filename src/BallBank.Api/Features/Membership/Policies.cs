using Marten;
using Microsoft.AspNetCore.Authorization;

namespace BallBank.Api.Features.Membership;

public static class Policies
{
    /// <summary>The caller is a member of the league in the route.</summary>
    public const string LeagueMember = "LeagueMember";

    public static IServiceCollection AddLeaguePolicies(this IServiceCollection services)
    {
        services.AddScoped<IAuthorizationHandler, LeagueMemberHandler>();
        return services.AddAuthorization(options => options.AddPolicy(LeagueMember, policy => policy
            .RequireAuthenticatedUser()
            .AddRequirements(new LeagueMemberRequirement())));
    }
}

/// <summary>The caller's <see cref="UserMemberships"/> lists the league named by the <c>leagueId</c> route argument.</summary>
public sealed class LeagueMemberRequirement : IAuthorizationRequirement;

/// <summary>
/// The fast gate in front of every league's pages (ADR-0011). It reads only the caller's memberships,
/// so a league the caller is not in is refused the same way whether it exists or not, and a guessed
/// league id reveals nothing.
/// </summary>
public sealed class LeagueMemberHandler(IDocumentStore store) : AuthorizationHandler<LeagueMemberRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, LeagueMemberRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true
            || context.Resource is not HttpContext http
            || LeagueTenant.Of(http) is not { } league)
        {
            return;
        }

        // UserMemberships lives in the default tenant, not the league's.
        await using var session = store.QuerySession();
        var memberships = await session.LoadAsync<UserMemberships>(context.User.Subject(), http.RequestAborted);

        if (memberships?.Leagues.Any(l => l.LeagueId.ToString() == league) == true)
        {
            context.Succeed(requirement);
        }
    }
}

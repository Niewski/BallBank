using System.Security.Claims;

namespace BallBank.Api;

public static class Identity
{
    /// <summary>
    /// The sign-in subject: the only claim BallBank reads from a token. Inbound claim mapping is off,
    /// so it arrives as <c>sub</c>.
    /// </summary>
    public static string Subject(this ClaimsPrincipal user) =>
        user.FindFirstValue("sub") ?? throw new InvalidOperationException("The token carries no subject.");
}

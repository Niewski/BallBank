using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Marten;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using Wolverine.Http;

namespace BallBank.Api;

/// <summary>
/// Runs in front of every endpoint that takes an <see cref="IdempotentRequest"/> (<see cref="Idempotency.UseIdempotency"/>).
/// Records are looked up in the league in the route; a route without one would use the default tenant.
/// </summary>
public static class IdempotencyMiddleware
{
    /// <summary>
    /// <c>412</c> without a key; the stored answer (or <c>422</c>) for a key already answered; otherwise
    /// on to the endpoint, which records its answer under the key.
    /// </summary>
    public static async Task<(IResult, IdempotentRequest?)> BeforeAsync(
        HttpContext context,
        ClaimsPrincipal user,
        IDocumentStore store,
        IOptions<JsonOptions> json,
        CancellationToken cancellation)
    {
        var key = context.Request.Headers[Idempotency.Header].ToString();
        if (string.IsNullOrWhiteSpace(key))
        {
            return (Results.Problem(
                statusCode: StatusCodes.Status412PreconditionFailed,
                title: $"{Idempotency.Header} required",
                detail: $"Send an {Idempotency.Header} header, fresh for each submission and the same when retrying it."), null);
        }

        var request = new IdempotentRequest(
            IdempotencyRecord.IdFor(user.Subject(), key),
            await FingerprintAsync(context.Request, cancellation),
            json.Value.SerializerOptions);

        await using var session = LeagueTenant.Of(context) is { } league ? store.QuerySession(league) : store.QuerySession();
        return await request.ReplayAsync(session, cancellation) is { } replay
            ? (replay, null)
            : (WolverineContinue.Result(), request);
    }

    // The method, path and body, hashed; the body is buffered so the endpoint can still read it.
    private static async Task<string> FingerprintAsync(HttpRequest request, CancellationToken cancellation)
    {
        request.EnableBuffering();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{request.Method} {request.Path}{request.QueryString}\n"));

        var buffer = new byte[4096];
        int read;
        while ((read = await request.Body.ReadAsync(buffer, cancellation)) > 0)
        {
            hash.AppendData(buffer, 0, read);
        }

        request.Body.Position = 0;
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}

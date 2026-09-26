using System.Net.Mime;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JasperFx;
using Marten;
using Marten.Exceptions;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using Wolverine.Http;

namespace BallBank.Api;

/// <summary>
/// The HTTP edge of ADR-0005: every mutating request carries an <c>Idempotency-Key</c>. An endpoint
/// opts in by taking an <see cref="IdempotentRequest"/> parameter, which <see cref="IdempotencyMiddleware"/>
/// supplies once the key is present and has not been used before.
/// </summary>
public static class Idempotency
{
    public const string Header = "Idempotency-Key";

    /// <summary>Runs <see cref="IdempotencyMiddleware"/> in front of every endpoint that takes an <see cref="IdempotentRequest"/>.</summary>
    public static void UseIdempotency(this WolverineHttpOptions options) =>
        options.AddMiddleware(typeof(IdempotencyMiddleware), chain =>
            chain.Method.Method.GetParameters().Any(parameter => parameter.ParameterType == typeof(IdempotentRequest)));
}

/// <summary>
/// A mutating request that carried an <see cref="Idempotency.Header"/> not yet answered. The endpoint
/// commits through <see cref="CommitAsync"/>, which stores the answer in the same transaction as the
/// events, so an answer is kept only if the command committed.
/// </summary>
public sealed class IdempotentRequest(string recordId, string fingerprint, JsonSerializerOptions json)
{
    /// <summary>
    /// Commits <paramref name="session"/> with the answer stored alongside, and returns the answer. If
    /// another request committed the same facts first, answers as that request did when it used this
    /// key (a retry that won the race), and with <paramref name="onCollision"/> otherwise.
    /// </summary>
    public async Task<IResult> CommitAsync(
        IDocumentSession session,
        int status,
        object body,
        Func<IResult> onCollision,
        CancellationToken cancellation)
    {
        var record = new IdempotencyRecord
        {
            Id = recordId,
            Fingerprint = fingerprint,
            Status = status,
            Body = JsonSerializer.Serialize(body, body.GetType(), json),
            RecordedAt = DateTimeOffset.UtcNow,
        };
        session.Insert(record);

        try
        {
            await session.SaveChangesAsync(cancellation);
        }
        catch (Exception exception) when (IsCollision(exception))
        {
            await using var committed = session.DocumentStore.QuerySession(session.TenantId);
            return await ReplayAsync(committed, cancellation) ?? onCollision();
        }

        return Replay(record);
    }

    /// <summary>
    /// The stored answer to this key, or <c>422</c> if the key was first used for a different request;
    /// <c>null</c> if the key has not been answered.
    /// </summary>
    public async Task<IResult?> ReplayAsync(IQuerySession session, CancellationToken cancellation)
    {
        var record = await session.LoadAsync<IdempotencyRecord>(recordId, cancellation);
        if (record is null)
        {
            return null;
        }

        return record.Fingerprint == fingerprint
            ? Replay(record)
            : Results.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Idempotency-Key reused",
                detail: $"This {Idempotency.Header} was already used for a different request. Send a fresh one for each submission.");
    }

    private static IResult Replay(IdempotencyRecord record) =>
        Results.Text(record.Body, MediaTypeNames.Application.Json, Encoding.UTF8, record.Status);

    // A stream or document that another transaction wrote first. Marten reports a stream id taken
    // mid-transaction as a unique violation wrapped in a command failure, not as a collision.
    private static bool IsCollision(Exception exception) =>
        exception is ExistingStreamIdCollisionException or DocumentAlreadyExistsException
        || exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}

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
                title: "Idempotency-Key required",
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

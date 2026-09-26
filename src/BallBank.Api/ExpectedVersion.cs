namespace BallBank.Api;

/// <summary>
/// Layer three of ADR-0005: a command about an existing stream carries, in its body, the stream
/// <c>version</c> the client last read. The command is decided only if nobody has written to the stream
/// since; otherwise the client re-reads and the person tries again.
/// </summary>
public static class ExpectedVersion
{
    /// <summary>The problem type of a stale version.</summary>
    public const string ConflictType = "version-conflict";

    /// <summary><c>412</c>: the request did not say which version it was decided against.</summary>
    public static IResult Required() => Results.Problem(
        statusCode: StatusCodes.Status412PreconditionFailed,
        title: "Version required",
        detail: "Send the version you last read, so a change made since is not overwritten.");

    /// <summary><c>409</c> of type <see cref="ConflictType"/>, with the stream's version now.</summary>
    public static IResult Conflict(long currentVersion) => Results.Problem(
        statusCode: StatusCodes.Status409Conflict,
        type: ConflictType,
        title: "Changed since you read it",
        detail: "Someone else made a change since you read it. Reload and try again.",
        extensions: new Dictionary<string, object?> { ["currentVersion"] = currentVersion });
}

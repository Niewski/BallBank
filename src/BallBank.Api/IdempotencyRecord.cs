namespace BallBank.Api;

/// <summary>
/// The answer a mutating request got, kept so a retry with the same <see cref="Idempotency.Header"/>
/// gets it again (ADR-0005). Tenant-scoped like every document, so the league is part of its identity;
/// the id holds the caller's subject and their key. Written in the same session as the events the
/// request recorded, so one exists only if the command committed. Kept for now; purging old records is
/// an operational task.
/// </summary>
public sealed class IdempotencyRecord
{
    public string Id { get; set; } = string.Empty;

    /// <summary>A hash of the method, path and body the key was first used with.</summary>
    public string Fingerprint { get; set; } = string.Empty;

    public int Status { get; set; }

    /// <summary>The response body, as JSON.</summary>
    public string Body { get; set; } = string.Empty;

    public DateTimeOffset RecordedAt { get; set; }

    /// <summary>One id per caller and key, unambiguous whatever characters either holds.</summary>
    public static string IdFor(string subject, string key) => $"{subject.Length}:{subject}:{key}";
}

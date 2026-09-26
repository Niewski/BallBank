namespace BallBank.Api;

/// <summary>
/// Well-known Marten event header keys. A header carries context the event body itself does not need
/// (see <see cref="Features.Treasury.OpenSeasonEndpoint"/>), so the history keeps it without widening
/// the event's own shape.
/// </summary>
public static class EventHeaders
{
    /// <summary>
    /// The caller's sign-in subject. <see cref="Features.Treasury.OpenSeasonEndpoint"/> sets this on
    /// every event it writes; later handlers should reuse the same key rather than inventing their own.
    /// </summary>
    public const string Subject = "subject";
}

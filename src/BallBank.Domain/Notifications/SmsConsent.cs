namespace BallBank.Domain.Notifications;

/// <summary>
/// A member's agreement to be texted at one number, and when they gave it. Consent belongs to the
/// number, not to the member: it does not carry over to a new one.
/// </summary>
/// <param name="Phone">E.164, the number on record when consent was given.</param>
public sealed record SmsConsent(string Phone, DateTimeOffset GivenAt)
{
    /// <summary>
    /// The consent a member's own opt-in records at the number on record. Opting in again at the number
    /// already consented to keeps the original moment; a number with no consent yet is stamped
    /// <paramref name="now"/>.
    /// </summary>
    public static SmsConsent Give(string? phone, SmsConsent? existing, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            throw new DomainException("Text messages go to a specific number. Add your phone number first.");
        }

        return existing is not null && existing.Phone == phone ? existing : new SmsConsent(phone, now);
    }

    /// <summary>
    /// What is left of this consent once the member's number is <paramref name="phone"/>: itself if the
    /// number is the same, nothing if it changed or was removed, so a new number is never texted until
    /// the member opts in at it.
    /// </summary>
    public SmsConsent? AfterNumberChangedTo(string? phone) => phone == Phone ? this : null;

    /// <summary>
    /// Whether a member may be texted at <paramref name="phone"/>: they consented at that very number,
    /// and it has not been opted out, which overrides consent whenever it stands.
    /// </summary>
    public static bool PermitsTexting(SmsConsent? consent, string? phone, bool numberOptedOut) =>
        consent is not null && phone is not null && consent.Phone == phone && !numberOptedOut;
}

namespace BallBank.Domain.Notifications;

public enum SmsOutcome
{
    /// <summary>Text the member now.</summary>
    Send,

    /// <summary>Never text them this.</summary>
    Skip,

    /// <summary>Text them once their quiet hours end.</summary>
    Hold,
}

/// <summary>
/// What to do with a text for one member, decided when it is about to be sent rather than when the event
/// happened: consent can be given or withdrawn, and a number can reply STOP, in between (ADR-0008).
/// </summary>
/// <param name="Reason">Why it is skipped or held; <c>null</c> when it is sent.</param>
/// <param name="SendAfter">When quiet hours end; <c>null</c> unless it is held.</param>
public sealed record SmsDelivery(SmsOutcome Outcome, string? Reason, DateTimeOffset? SendAfter)
{
    public const string NoConsent = "No consent to text this number";
    public const string OptedOut = "This number has opted out of texts";
    public const string InQuietHours = "In the member's quiet hours";

    /// <summary>An opted-out number is never texted, nor one without consent; otherwise now, or when quiet hours end.</summary>
    public static SmsDelivery Decide(
        SmsConsent? consent, string? phone, bool numberOptedOut, QuietHours quietHours, DateTimeOffset now)
    {
        if (numberOptedOut)
        {
            return new SmsDelivery(SmsOutcome.Skip, OptedOut, null);
        }

        if (!SmsConsent.PermitsTexting(consent, phone, false))
        {
            return new SmsDelivery(SmsOutcome.Skip, NoConsent, null);
        }

        return quietHours.HeldUntil(now) is { } until
            ? new SmsDelivery(SmsOutcome.Hold, InQuietHours, until)
            : new SmsDelivery(SmsOutcome.Send, null, null);
    }
}

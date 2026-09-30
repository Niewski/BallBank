using BallBank.Domain.Notifications;

namespace BallBank.Api.Features.Notifications;

/// <summary>
/// What one member has said about being texted: whether they consented, at which number and when, and
/// the hours they want left quiet. A document rather than events, like contact details (ADR-0012);
/// tenant-scoped, keyed by member, and written only by the member who holds it.
/// </summary>
public sealed class NotificationPreferences
{
    /// <summary>The member id.</summary>
    public Guid Id { get; set; }

    /// <summary>E.164, the number consent was given for; <c>null</c> when there is none.</summary>
    public string? ConsentPhone { get; set; }

    public DateTimeOffset? ConsentGivenAt { get; set; }

    public int QuietStartHour { get; set; } = QuietHours.Default.StartHour;

    public int QuietEndHour { get; set; } = QuietHours.Default.EndHour;

    /// <summary>An IANA id.</summary>
    public string QuietTimeZone { get; set; } = QuietHours.Default.TimeZone;

    public SmsConsent? ToConsent() =>
        ConsentPhone is not null && ConsentGivenAt is { } givenAt ? new SmsConsent(ConsentPhone, givenAt) : null;

    public QuietHours ToQuietHours() => new(QuietStartHour, QuietEndHour, QuietTimeZone);

    public static NotificationPreferences Of(Guid memberId, SmsConsent? consent, QuietHours quietHours) => new()
    {
        Id = memberId,
        ConsentPhone = consent?.Phone,
        ConsentGivenAt = consent?.GivenAt,
        QuietStartHour = quietHours.StartHour,
        QuietEndHour = quietHours.EndHour,
        QuietTimeZone = quietHours.TimeZone,
    };

    public bool ConsentLapsesWhenNumberChangesTo(string? phone) =>
        ToConsent() is { } consent && consent.AfterNumberChangedTo(phone) is null;

    public NotificationPreferences WithoutConsent() => Of(Id, consent: null, ToQuietHours());
}

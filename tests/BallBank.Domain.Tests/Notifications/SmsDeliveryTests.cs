using BallBank.Domain.Notifications;

namespace BallBank.Domain.Tests.Notifications;

public class SmsDeliveryTests
{
    private const string Sams = "+15550100002";
    private const string SamsNewNumber = "+15550100009";

    // 2026-09-24 16:00 UTC is noon in New York, outside the default quiet hours (9pm to 9am).
    private static readonly DateTimeOffset Midday = new(2026, 9, 24, 16, 0, 0, TimeSpan.Zero);

    // 2026-09-24 02:00 UTC is 10pm on the 23rd in New York, inside them.
    private static readonly DateTimeOffset Night = new(2026, 9, 24, 2, 0, 0, TimeSpan.Zero);

    private static readonly SmsConsent Consent = new(Sams, new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void A_member_who_consented_at_their_number_is_texted_at_once_outside_quiet_hours()
    {
        SmsDelivery.Decide(Consent, Sams, numberOptedOut: false, QuietHours.Default, Midday)
            .ShouldBe(new SmsDelivery(SmsOutcome.Send, Reason: null, SendAfter: null));
    }

    [Fact]
    public void A_member_who_never_opted_in_is_skipped()
    {
        SmsDelivery.Decide(consent: null, Sams, numberOptedOut: false, QuietHours.Default, Midday)
            .ShouldBe(new SmsDelivery(SmsOutcome.Skip, SmsDelivery.NoConsent, SendAfter: null));
    }

    [Fact]
    public void A_member_with_no_number_is_skipped()
    {
        SmsDelivery.Decide(Consent, phone: null, numberOptedOut: false, QuietHours.Default, Midday)
            .ShouldBe(new SmsDelivery(SmsOutcome.Skip, SmsDelivery.NoConsent, SendAfter: null));
    }

    [Fact]
    public void Consent_for_the_old_number_does_not_cover_the_new_one()
    {
        SmsDelivery.Decide(Consent, SamsNewNumber, numberOptedOut: false, QuietHours.Default, Midday)
            .ShouldBe(new SmsDelivery(SmsOutcome.Skip, SmsDelivery.NoConsent, SendAfter: null));
    }

    [Fact]
    public void A_number_that_replied_STOP_is_skipped_however_the_member_consented()
    {
        SmsDelivery.Decide(Consent, Sams, numberOptedOut: true, QuietHours.Default, Midday)
            .ShouldBe(new SmsDelivery(SmsOutcome.Skip, SmsDelivery.OptedOut, SendAfter: null));
    }

    [Fact]
    public void An_opt_out_is_the_reason_even_when_there_is_no_consent()
    {
        SmsDelivery.Decide(consent: null, Sams, numberOptedOut: true, QuietHours.Default, Midday)
            .ShouldBe(new SmsDelivery(SmsOutcome.Skip, SmsDelivery.OptedOut, SendAfter: null));
    }

    [Fact]
    public void A_text_that_falls_in_quiet_hours_is_held_until_they_end()
    {
        // Quiet hours end at 9am New York time: 13:00 UTC on the 24th.
        SmsDelivery.Decide(Consent, Sams, numberOptedOut: false, QuietHours.Default, Night)
            .ShouldBe(new SmsDelivery(SmsOutcome.Hold, SmsDelivery.InQuietHours, new DateTimeOffset(2026, 9, 24, 13, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Quiet_hours_do_not_hold_a_member_who_was_never_to_be_texted()
    {
        SmsDelivery.Decide(consent: null, Sams, numberOptedOut: false, QuietHours.Default, Night)
            .Outcome.ShouldBe(SmsOutcome.Skip);
    }

    [Fact]
    public void Quiet_hours_are_the_members_own()
    {
        // 10pm in New York is 7pm in Los Angeles: a member whose quiet hours are in Los Angeles is not asleep yet.
        SmsDelivery.Decide(Consent, Sams, numberOptedOut: false, new QuietHours(21, 9, "America/Los_Angeles"), Night)
            .Outcome.ShouldBe(SmsOutcome.Send);
    }
}

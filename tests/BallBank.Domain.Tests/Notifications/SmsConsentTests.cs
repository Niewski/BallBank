using BallBank.Domain.Notifications;

namespace BallBank.Domain.Tests.Notifications;

public class SmsConsentTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Earlier = Now.AddDays(-3);

    private const string Sams = "+15550100002";
    private const string SamsNewNumber = "+15550100009";

    [Fact]
    public void Consent_is_given_for_the_number_on_record_and_says_when()
    {
        SmsConsent.Give(Sams, existing: null, Now).ShouldBe(new SmsConsent(Sams, Now));
    }

    [Fact]
    public void Consent_needs_a_number_to_be_for()
    {
        Should.Throw<DomainException>(() => SmsConsent.Give(phone: null, existing: null, Now))
            .Message.ShouldBe("Text messages go to a specific number. Add your phone number first.");
    }

    [Fact]
    public void Consent_needs_a_number_that_is_not_blank()
    {
        Should.Throw<DomainException>(() => SmsConsent.Give("  ", existing: null, Now));
    }

    [Fact]
    public void Giving_consent_again_at_the_same_number_keeps_when_it_was_first_given()
    {
        var first = new SmsConsent(Sams, Earlier);

        SmsConsent.Give(Sams, first, Now).ShouldBe(first);
    }

    [Fact]
    public void Consent_given_for_another_number_is_not_consent_for_this_one()
    {
        var forTheOldNumber = new SmsConsent(Sams, Earlier);

        SmsConsent.Give(SamsNewNumber, forTheOldNumber, Now).ShouldBe(new SmsConsent(SamsNewNumber, Now));
    }

    [Fact]
    public void A_changed_number_clears_consent()
    {
        new SmsConsent(Sams, Earlier).AfterNumberChangedTo(SamsNewNumber).ShouldBeNull();
    }

    [Fact]
    public void Removing_the_number_clears_consent()
    {
        new SmsConsent(Sams, Earlier).AfterNumberChangedTo(null).ShouldBeNull();
    }

    [Fact]
    public void Saving_the_same_number_again_keeps_consent()
    {
        var consent = new SmsConsent(Sams, Earlier);

        consent.AfterNumberChangedTo(Sams).ShouldBe(consent);
    }

    [Fact]
    public void Consent_permits_texting_at_the_number_it_was_given_for()
    {
        SmsConsent.PermitsTexting(new SmsConsent(Sams, Earlier), Sams, numberOptedOut: false).ShouldBeTrue();
    }

    [Fact]
    public void Nobody_is_texted_who_gave_no_consent()
    {
        SmsConsent.PermitsTexting(consent: null, Sams, numberOptedOut: false).ShouldBeFalse();
    }

    [Fact]
    public void Consent_does_not_permit_texting_at_a_different_number()
    {
        SmsConsent.PermitsTexting(new SmsConsent(Sams, Earlier), SamsNewNumber, numberOptedOut: false).ShouldBeFalse();
    }

    [Fact]
    public void Consent_does_not_permit_texting_a_member_with_no_number()
    {
        SmsConsent.PermitsTexting(new SmsConsent(Sams, Earlier), phone: null, numberOptedOut: false).ShouldBeFalse();
    }

    [Fact]
    public void An_opt_out_for_the_number_overrides_consent()
    {
        SmsConsent.PermitsTexting(new SmsConsent(Sams, Earlier), Sams, numberOptedOut: true).ShouldBeFalse();
    }
}

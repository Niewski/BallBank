using BallBank.Api.Integrations.Twilio;

namespace BallBank.Integration.Tests.Twilio;

public class TwilioSignatureTests
{
    // Twilio's documented example, with fake numbers; the signature is HMAC-SHA1 by openssl, not by the code under test.
    private const string Url = "https://mycompany.com/myapp.php?foo=1&bar=2";
    private const string AuthToken = "12345";
    private const string Signature = "xjb0XIgK5NLhElzR4tMnDcxzHDA=";

    private static readonly KeyValuePair<string, string>[] Form =
    [
        new("To", "+15550100010"),
        new("Digits", "1234"),
        new("From", "+15550100011"),
        new("Caller", "+15550100011"),
        new("CallSid", "CA1234567890ABCDE"),
    ];

    [Fact]
    public void A_request_signed_as_Twilio_documents_it_is_valid()
    {
        TwilioSignature.IsValid(Url, Form, AuthToken, Signature).ShouldBeTrue();
    }

    [Fact]
    public void The_signature_does_not_depend_on_the_order_the_parameters_arrive_in()
    {
        TwilioSignature.IsValid(Url, Form.Reverse(), AuthToken, Signature).ShouldBeTrue();
    }

    [Fact]
    public void A_signature_made_with_another_token_is_not_valid()
    {
        TwilioSignature.IsValid(Url, Form, "54321", Signature).ShouldBeFalse();
    }

    [Fact]
    public void A_changed_parameter_is_not_valid()
    {
        var changed = Form.Select(p => p.Key == "Digits" ? new KeyValuePair<string, string>("Digits", "9999") : p);

        TwilioSignature.IsValid(Url, changed, AuthToken, Signature).ShouldBeFalse();
    }

    [Fact]
    public void An_added_parameter_is_not_valid()
    {
        TwilioSignature.IsValid(Url, [.. Form, new("Extra", "1")], AuthToken, Signature).ShouldBeFalse();
    }

    [Fact]
    public void A_different_address_is_not_valid()
    {
        TwilioSignature.IsValid("https://mycompany.com/myapp.php?foo=1&bar=3", Form, AuthToken, Signature).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("AAAA")]
    public void A_missing_or_malformed_signature_is_not_valid(string? signature)
    {
        TwilioSignature.IsValid(Url, Form, AuthToken, signature).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Without_an_auth_token_nothing_is_valid(string? authToken)
    {
        TwilioSignature.IsValid(Url, Form, authToken, Signature).ShouldBeFalse();
    }
}

using System.Security.Cryptography;
using System.Text;

namespace BallBank.Api.Integrations.Twilio;

/// <summary>Checks <c>X-Twilio-Signature</c>: HMAC-SHA1 under the auth token of the address plus each form field's name and value, by name.</summary>
public static class TwilioSignature
{
    public const string Header = "X-Twilio-Signature";

    public static bool IsValid(
        string url, IEnumerable<KeyValuePair<string, string>> form, string? authToken, string? signature)
    {
        if (string.IsNullOrWhiteSpace(authToken) || string.IsNullOrEmpty(signature))
        {
            return false;
        }

        byte[] given;
        try
        {
            given = Convert.FromBase64String(signature);
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = Compute(url, form, authToken);
        return CryptographicOperations.FixedTimeEquals(given, expected);
    }

    private static byte[] Compute(string url, IEnumerable<KeyValuePair<string, string>> form, string authToken)
    {
        var signed = new StringBuilder(url);
        foreach (var (name, value) in form.OrderBy(parameter => parameter.Key, StringComparer.Ordinal).ThenBy(parameter => parameter.Value, StringComparer.Ordinal))
        {
            signed.Append(name).Append(value);
        }

        return HMACSHA1.HashData(Encoding.UTF8.GetBytes(authToken), Encoding.UTF8.GetBytes(signed.ToString()));
    }
}

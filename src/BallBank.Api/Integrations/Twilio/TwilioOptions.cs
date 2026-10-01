namespace BallBank.Api.Integrations.Twilio;

/// <summary>The <c>Twilio</c> section of configuration: user-secrets locally, platform settings deployed, never the repository.</summary>
public sealed class TwilioOptions
{
    public const string Section = "Twilio";

    public string? AccountSid { get; set; }

    /// <summary>A secret. Never logged, traced or put in an exception.</summary>
    public string? AuthToken { get; set; }

    /// <summary>The one toll-free number every text is sent from, E.164.</summary>
    public string? FromNumber { get; set; }

    /// <summary>Where this API is reached from the internet, which Twilio reports a text's delivery back to.</summary>
    public string? StatusCallbackBaseUrl { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccountSid)
        && !string.IsNullOrWhiteSpace(AuthToken)
        && !string.IsNullOrWhiteSpace(FromNumber)
        && !string.IsNullOrWhiteSpace(StatusCallbackBaseUrl);
}

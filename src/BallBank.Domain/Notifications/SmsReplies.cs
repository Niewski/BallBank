namespace BallBank.Domain.Notifications;

/// <summary>What a member's reply to a text asks of BallBank.</summary>
public enum SmsReply
{
    /// <summary>Not an instruction; BallBank does not read replies.</summary>
    Other,

    /// <summary>Stop texting this number, in every league.</summary>
    Stop,

    /// <summary>Resume texting this number, where consent is on record.</summary>
    Start,
}

public static class SmsReplies
{
    // Twilio's default keywords: it blocks and unblocks the number itself on these, so BallBank must agree.
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "STOP", "STOPALL", "UNSUBSCRIBE", "CANCEL", "END", "QUIT",
    };

    private static readonly HashSet<string> StartWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "START", "YES", "UNSTOP",
    };

    /// <summary>A reply is an instruction only when it is nothing but the keyword, in any case.</summary>
    public static SmsReply Classify(string? body)
    {
        var word = body?.Trim() ?? string.Empty;
        if (StopWords.Contains(word))
        {
            return SmsReply.Stop;
        }

        return StartWords.Contains(word) ? SmsReply.Start : SmsReply.Other;
    }
}

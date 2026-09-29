using System.Text.RegularExpressions;

namespace BallBank.Domain.Notifications;

/// <summary>
/// What BallBank accepts as a Discord webhook. The address is pasted in by a treasurer and the API
/// posts to it, so it must be Discord's and nobody else's: anything else would let a treasurer point the
/// API at any address it can reach.
/// </summary>
public static partial class DiscordWebhook
{
    /// <summary>Where Discord serves webhooks.</summary>
    public static readonly IReadOnlyList<string> Hosts = ["discord.com", "discordapp.com", "ptb.discord.com", "canary.discord.com"];

    /// <summary>
    /// The webhook as it will be kept: <c>https://host/api/webhooks/{id}/{token}</c>, with anything Discord
    /// lets a URL carry beyond that dropped. Throws <see cref="DomainException"/> for anything that is not
    /// a webhook on one of <paramref name="hosts"/>.
    /// </summary>
    public static string Accept(string? url, IReadOnlyCollection<string> hosts)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort
            || uri.UserInfo.Length > 0
            || !hosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase)
            || !WebhookPath().IsMatch(uri.AbsolutePath))
        {
            throw new DomainException(
                "That is not a Discord webhook. In Discord, open the channel's settings, then Integrations, then Webhooks, "
                + "and copy the webhook URL: it starts with https://discord.com/api/webhooks/.");
        }

        return $"https://{uri.Host}{uri.AbsolutePath}";
    }

    /// <summary>The last characters of the webhook: enough to recognise it by, too little to use.</summary>
    public static string Tail(string webhookUrl) => webhookUrl[^4..];

    [GeneratedRegex(@"^/api/webhooks/\d+/[A-Za-z0-9_-]+$")]
    private static partial Regex WebhookPath();
}

using BallBank.Domain.Notifications;

namespace BallBank.Api.Features.Notifications;

/// <summary>The <c>Notifications</c> section of configuration. Everything has a default; a host sets only what it changes.</summary>
public sealed class NotificationOptions
{
    public const string Section = "Notifications";

    /// <summary>How long to wait before each retry of a send that failed, after which it is dead-lettered.</summary>
    public static readonly TimeSpan[] DefaultRetryDelays = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5)];

    /// <summary>Where the web app is served, which the links in a text point to: <c>/statement?league=&amp;account=</c> on it.</summary>
    public string WebBaseUrl { get; set; } = string.Empty;

    /// <summary>The hosts a Discord webhook may be on; Discord's own unless a host says otherwise.</summary>
    public string[]? DiscordHosts { get; set; }

    public TimeSpan[]? RetryDelays { get; set; }

    public IReadOnlyCollection<string> AllowedDiscordHosts => DiscordHosts ?? [.. DiscordWebhook.Hosts];

    public TimeSpan[] Delays => RetryDelays ?? DefaultRetryDelays;
}

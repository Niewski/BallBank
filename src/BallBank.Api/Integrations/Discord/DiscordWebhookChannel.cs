using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using BallBank.Api.Features.Notifications;
using BallBank.Domain.Notifications;
using Polly;

namespace BallBank.Api.Integrations.Discord;

/// <summary>
/// Posts to a Discord channel through its webhook (https://discord.com/developers/docs/resources/webhook).
/// Rides the resilient <see cref="HttpClient"/> from ServiceDefaults, so timeouts and retries come from there.
/// The webhook URL is a secret, so it is never logged, traced or put in an exception; see
/// <see cref="DiscordRegistration"/> for what that takes.
/// </summary>
public sealed class DiscordWebhookChannel(HttpClient http) : INotificationChannel
{
    // Named after the assembly, which is the source ServiceDefaults subscribes to.
    private static readonly ActivitySource Tracing = new(typeof(DiscordWebhookChannel).Assembly.GetName().Name!);

    public string Name => Channels.Discord;

    public async Task SendAsync(string destination, string text, CancellationToken cancellation)
    {
        using var activity = Tracing.StartActivity("Discord webhook post");

        try
        {
            // No mention in the text can ping anybody: a team called "@everyone" is only a name.
            using var response = await http.PostAsJsonAsync(
                destination,
                new WebhookMessage(text, new AllowedMentions([])),
                cancellation);

            if (!response.IsSuccessStatusCode)
            {
                throw new NotificationDeliveryException($"Discord did not accept the notification: it answered {(int)response.StatusCode}.");
            }
        }
        catch (Exception failure) when (failure is HttpRequestException or ExecutionRejectedException
            || (failure is OperationCanceledException && !cancellation.IsCancellationRequested))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Discord did not answer.");
            throw new NotificationDeliveryException("Discord did not accept the notification: it did not answer.", failure);
        }
        catch (NotificationDeliveryException refusal)
        {
            activity?.SetStatus(ActivityStatusCode.Error, refusal.Message);
            throw;
        }
    }

    private sealed record WebhookMessage(string Content, [property: JsonPropertyName("allowed_mentions")] AllowedMentions AllowedMentions);

    private sealed record AllowedMentions(string[] Parse);
}

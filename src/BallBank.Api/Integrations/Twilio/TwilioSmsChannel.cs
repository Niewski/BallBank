using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using BallBank.Api.Features.Notifications;
using BallBank.Domain.Notifications;
using Microsoft.Extensions.Options;
using Polly;

namespace BallBank.Api.Integrations.Twilio;

/// <summary>Texts a member through Twilio's Messages API. The auth token and the member's number never reach a log, a trace or an exception.</summary>
public sealed class TwilioSmsChannel(HttpClient http, IOptions<TwilioOptions> options) : INotificationChannel
{
    public const string StatusCallbackPath = "/webhooks/twilio/status";

    // Named after the assembly, which is the source ServiceDefaults subscribes to.
    private static readonly ActivitySource Tracing = new(typeof(TwilioSmsChannel).Assembly.GetName().Name!);

    public string Name => Channels.Sms;

    /// <summary>Where Twilio reports on one notification's text. The notification's id has slashes in it, so it is escaped.</summary>
    public static string StatusCallback(string baseUrl, Guid leagueId, string notificationId) =>
        $"{baseUrl.TrimEnd('/')}{StatusCallbackPath}?league={leagueId}&notification={Uri.EscapeDataString(notificationId)}";

    public async Task SendAsync(OutgoingNotification message, CancellationToken cancellation)
    {
        var settings = options.Value;
        if (!settings.IsConfigured)
        {
            throw new NotificationDeliveryException("Twilio is not configured: it needs an account SID, an auth token, a number to send from and the API's public address.");
        }

        using var activity = Tracing.StartActivity("Twilio SMS send");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"2010-04-01/Accounts/{Uri.EscapeDataString(settings.AccountSid!)}/Messages.json")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["To"] = message.Destination,
                ["From"] = settings.FromNumber!,
                ["Body"] = message.Text,
                ["StatusCallback"] = StatusCallback(settings.StatusCallbackBaseUrl!, message.LeagueId, message.NotificationId),
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.AccountSid}:{settings.AuthToken}")));

        try
        {
            using var response = await http.SendAsync(request, cancellation);

            if (!response.IsSuccessStatusCode)
            {
                throw new NotificationDeliveryException($"Twilio did not accept the notification: it answered {(int)response.StatusCode}.");
            }
        }
        catch (Exception failure) when (failure is HttpRequestException or ExecutionRejectedException
            || (failure is OperationCanceledException && !cancellation.IsCancellationRequested))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Twilio did not answer.");
            throw new NotificationDeliveryException("Twilio did not accept the notification: it did not answer.", failure);
        }
        catch (NotificationDeliveryException refusal)
        {
            activity?.SetStatus(ActivityStatusCode.Error, refusal.Message);
            throw;
        }
    }
}

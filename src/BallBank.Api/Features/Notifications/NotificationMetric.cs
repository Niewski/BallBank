using System.Diagnostics.Metrics;

namespace BallBank.Api.Features.Notifications;

/// <summary>
/// What became of each attempt to deliver a notification (ADR-0008), counted by league (<c>tenant.id</c>), channel,
/// kind and outcome: a <see cref="NotificationStatus"/> (Sent, Held, Skipped or Dropped), or <see cref="Failed"/> when
/// the channel refused the send, which is tried again. That is not <see cref="NotificationStatus.Failed"/>, which is
/// Twilio reporting later that it could not send.
/// </summary>
public static class NotificationMetric
{
    public const string MeterName = "BallBank.Notifications";
    public const string InstrumentName = "ballbank.notifications";

    public const string Channel = "channel";
    public const string Kind = "kind";
    public const string Outcome = "outcome";

    public const string Failed = "Failed";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Attempts = Meter.CreateCounter<long>(
        InstrumentName,
        unit: "{notification}",
        description: "Attempts to deliver a notification, by what became of them.");

    public static void Record(Guid leagueId, Notification notification, string outcome) =>
        Attempts.Add(
            1,
            new KeyValuePair<string, object?>(TenantTelemetry.TenantId, leagueId.ToString()),
            new KeyValuePair<string, object?>(Channel, notification.Channel),
            new KeyValuePair<string, object?>(Kind, notification.Kind),
            new KeyValuePair<string, object?>(Outcome, outcome));
}

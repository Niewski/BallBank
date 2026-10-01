namespace BallBank.Domain.Notifications;

/// <summary>Where a text Twilio accepted has got to, as Twilio reports it.</summary>
public enum TextDelivery
{
    /// <summary>Queued, being sent, or sent to the carrier: not settled either way.</summary>
    InFlight,

    Delivered,

    /// <summary>The carrier did not deliver it.</summary>
    Undelivered,

    /// <summary>Twilio could not send it.</summary>
    Failed,
}

public static class TextDeliveries
{
    /// <summary>What Twilio's <c>MessageStatus</c> says; <c>null</c> when it says nothing about an outgoing text.</summary>
    public static TextDelivery? Read(string? messageStatus) => messageStatus?.Trim().ToLowerInvariant() switch
    {
        "accepted" or "scheduled" or "queued" or "sending" or "sent" => TextDelivery.InFlight,
        "delivered" => TextDelivery.Delivered,
        "undelivered" => TextDelivery.Undelivered,
        "failed" or "canceled" => TextDelivery.Failed,
        _ => null,
    };
}

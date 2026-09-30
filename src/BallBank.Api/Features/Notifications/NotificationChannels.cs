namespace BallBank.Api.Features.Notifications;

/// <summary>The channels BallBank can tell people things on, found by name. Transient: a channel may hold a pooled HTTP client.</summary>
public sealed class NotificationChannels(IEnumerable<INotificationChannel> channels)
{
    public INotificationChannel For(string name) =>
        channels.FirstOrDefault(channel => channel.Name == name)
        ?? throw new InvalidOperationException($"There is no notification channel called {name}.");
}

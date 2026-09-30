using BallBank.Api.Features.Notifications;

namespace BallBank.Api.Integrations.Discord;

public static class DiscordRegistration
{
    /// <summary>
    /// The typed <see cref="DiscordWebhookChannel"/>, as the <see cref="INotificationChannel"/> for Discord.
    /// ServiceDefaults adds the standard resilience handler to every client. The webhook URL carries its
    /// secret in the path, and the HTTP client factory logs the URL of every request it sends, so its
    /// loggers are removed from this client (and ServiceDefaults keeps webhook calls out of traces).
    /// </summary>
    public static IServiceCollection AddDiscord(this IServiceCollection services)
    {
        services.AddHttpClient<DiscordWebhookChannel>().RemoveAllLoggers();
        services.AddTransient<INotificationChannel>(services => services.GetRequiredService<DiscordWebhookChannel>());

        return services;
    }
}

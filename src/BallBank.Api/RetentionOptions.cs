namespace BallBank.Api;

/// <summary>
/// How long the tick keeps what nobody will ask for again: retried-request answers (<see cref="IdempotencyRecord"/>) and the
/// records of what was sent (<see cref="Features.Notifications.Notification"/>), so the free database tier does not fill (ADR-0005).
/// Set as <c>Retention__Days</c> on the platform; not required.
/// </summary>
public sealed class RetentionOptions
{
    public const string Section = "Retention";

    /// <summary>The shortest age allowed, twice the week a dedupe key has to hold for: a notification's id is its key, freed when it is purged.</summary>
    public const int MinimumDays = 14;

    /// <summary>How many days a record is kept, counting from when it was written.</summary>
    public int Days { get; set; } = 90;

    public TimeSpan Age => TimeSpan.FromDays(Days);

    public bool IsValid => Days >= MinimumDays;
}

public static class RetentionRegistration
{
    /// <summary>Binds <see cref="RetentionOptions"/>; the tick's host is never started, so there the first read of the age is what refuses a bad one.</summary>
    public static IServiceCollection AddRetention(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RetentionOptions>()
            .Bind(configuration.GetSection(RetentionOptions.Section))
            .Validate(options => options.IsValid, $"Retention:Days must be at least {RetentionOptions.MinimumDays}.")
            .ValidateOnStart();

        return services;
    }
}

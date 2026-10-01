namespace BallBank.Api;

/// <summary>
/// How many requests each party may make (ADR-0013). Settings under <c>RateLimits</c>, so a season can tune
/// them without a release, e.g. <c>RateLimits__League__Burst=400</c> on the platform. Every setting has a default,
/// generous for a league of a dozen people, so none is required.
/// </summary>
public sealed class RateLimitOptions
{
    public const string Section = "RateLimits";

    /// <summary>Every request under <c>/leagues/{leagueId}</c>, from anyone, spends the league's budget.</summary>
    public Budget League { get; set; } = new() { Burst = 200, Refill = 50 };

    /// <summary>A signed-in caller's requests outside any league (<c>/me/leagues</c>, Sleeper lookups).</summary>
    public Budget Caller { get; set; } = new() { Burst = 100, Refill = 20 };

    /// <summary>Everything under <c>/webhooks</c>, per remote address.</summary>
    public Budget Webhook { get; set; } = new() { Burst = 50, Refill = 10 };
}

/// <summary>
/// A token bucket: <see cref="Burst"/> requests at once, and <see cref="Refill"/> more every <see cref="Every"/>
/// until the bucket is full again.
/// </summary>
public sealed class Budget
{
    public int Burst { get; set; }

    public int Refill { get; set; }

    public TimeSpan Every { get; set; } = TimeSpan.FromSeconds(1);
}

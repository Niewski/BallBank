using Marten;

namespace BallBank.Api.Features.Notifications;

/// <summary>
/// A phone number that has asked to be texted no more, by replying STOP. Cross-tenant (ADR-0011): an
/// opt-out belongs to the number, not to any league, so it binds every league the number is in, and
/// overrides consent wherever it stands. Keyed by E.164.
/// </summary>
public sealed class PhoneOptOut
{
    /// <summary>E.164.</summary>
    public string Id { get; set; } = "";

    public DateTimeOffset OptedOutAt { get; set; }
}

public static class PhoneOptOuts
{
    /// <summary>The numbers among <paramref name="phones"/> that have opted out. They live in the default tenant, not the league's.</summary>
    public static async Task<HashSet<string>> AmongAsync(IDocumentStore store, IEnumerable<string> phones, CancellationToken cancellation)
    {
        var wanted = phones.Distinct().ToArray();
        if (wanted.Length == 0)
        {
            return [];
        }

        await using var session = store.QuerySession();
        var found = await session.LoadManyAsync<PhoneOptOut>(cancellation, wanted);
        return found.Select(o => o.Id).ToHashSet();
    }
}

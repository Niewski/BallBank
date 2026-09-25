using BallBank.Domain.Membership;

namespace BallBank.Api.Features.Membership;

/// <summary>
/// How to reach one member of a league: personal data, so a document rather than events (ADR-0012).
/// Tenant-scoped like every document, keyed by member. It exists only while something is recorded;
/// clearing every field deletes it, and with it everything BallBank kept about how to reach them.
/// </summary>
public sealed class MemberContact
{
    /// <summary>The member id.</summary>
    public Guid Id { get; set; }

    public string? Email { get; set; }

    /// <summary>E.164.</summary>
    public string? Phone { get; set; }

    public string? DiscordUsername { get; set; }

    public ContactDetails Details => new(Email, Phone, DiscordUsername);

    public static MemberContact Of(Guid memberId, ContactDetails details) => new()
    {
        Id = memberId,
        Email = details.Email,
        Phone = details.Phone,
        DiscordUsername = details.DiscordUsername,
    };
}

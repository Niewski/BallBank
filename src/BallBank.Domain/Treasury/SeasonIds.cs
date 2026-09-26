using System.Security.Cryptography;
using System.Text;

namespace BallBank.Domain.Treasury;

/// <summary>
/// Deterministic ids for a season and what opening it creates, computed from names rather than
/// generated, so opening a season twice — a retry, or a second treasurer's click — always names the
/// same stream and adds no events (ADR-0005).
/// </summary>
public static class SeasonIds
{
    // Fixed once, arbitrary: only its stability across builds matters (RFC 9562 §4, name-based ids).
    private static readonly Guid Namespace = Guid.Parse("2f57a1c1-8c7d-4c7e-9f1e-6e0a2f3d9b71");

    public static Guid SeasonId(Guid leagueId, string label) => NameBased($"season/{leagueId}/{label.Trim()}");

    public static Guid AccountId(Guid seasonId, Guid memberId) => NameBased($"account/{seasonId}/{memberId}");

    /// <summary>The one assessment id every member's season-dues line carries, so a retried open assesses nobody twice.</summary>
    public static Guid DuesAssessmentId(Guid seasonId) => NameBased($"dues/{seasonId}");

    // A version-5 (SHA-1, name-based) UUID, hand-rolled: BallBank.Domain takes no packages, and the
    // base class library has no built-in name-based UUID (only NewGuid and the random version 7).
    private static Guid NameBased(string name)
    {
        Span<byte> namespaceBytes = stackalloc byte[16];
        Namespace.TryWriteBytes(namespaceBytes, bigEndian: true, out _);

        var nameBytes = Encoding.UTF8.GetBytes(name);
        var input = new byte[namespaceBytes.Length + nameBytes.Length];
        namespaceBytes.CopyTo(input);
        nameBytes.CopyTo(input.AsSpan(namespaceBytes.Length));

        var hash = SHA1.HashData(input);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50); // version 5
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // RFC 9562 variant

        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }
}

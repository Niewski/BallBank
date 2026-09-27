using System.Globalization;

namespace BallBank.Domain;

/// <summary>
/// How amounts and dates read in anything written for a member: refusals and history sentences. The web
/// app's <c>lib/money.ts</c> and <c>lib/dates.ts</c> write them the same way.
/// </summary>
public static class Format
{
    /// <summary>US dollars and cents, without a sign: the sentence around it says which way it went.</summary>
    public static string Money(decimal amount) => $"${Math.Abs(amount).ToString("0.00", CultureInfo.InvariantCulture)}";

    /// <summary>A calendar date, e.g. <c>Oct 1, 2026</c>.</summary>
    public static string Date(DateOnly date) => date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
}

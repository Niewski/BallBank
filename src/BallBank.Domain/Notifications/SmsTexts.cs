using BallBank.Domain.Treasury;

namespace BallBank.Domain.Notifications;

/// <summary>
/// The texts BallBank sends a member or a treasurer about an account (ADR-0008). Every one names BallBank and
/// the league, so a stranger's text is never a mystery, and ends with the link to the statement it is about.
/// Rendered here, when the event is handled, so what was said is what was recorded.
/// </summary>
public static class SmsTexts
{
    /// <summary>Where the web app shows one account's statement, on the site at <paramref name="webBaseUrl"/>.</summary>
    public static string StatementLink(string webBaseUrl, Guid leagueId, Guid accountId) =>
        $"{webBaseUrl.TrimEnd('/')}/statement?league={leagueId}&account={accountId}";

    public static string DuesAssessed(string leagueName, decimal amount, string memo, DateOnly dueDate, string statementLink)
    {
        var forWhat = string.IsNullOrWhiteSpace(memo) ? "" : $" for {memo}";
        return $"BallBank: {leagueName} assessed you {Format.Money(amount)}{forWhat}, due {Format.Date(dueDate)}. {Statement(statementLink)}";
    }

    /// <summary>For a treasurer: <paramref name="payerName"/> says they paid, and it waits for the treasurer.</summary>
    public static string PaymentAttested(
        string leagueName, string payerName, decimal amount, PaymentRail rail, string? reference, string statementLink)
    {
        var referenced = string.IsNullOrWhiteSpace(reference) ? "" : $" ({reference})";
        return $"BallBank: {payerName} says they paid {Format.Money(amount)} by {rail}{referenced} in {leagueName}. Confirm or reject it: {statementLink}";
    }

    public static string PaymentConfirmed(string leagueName, decimal amount, PaymentRail rail, string statementLink) =>
        $"BallBank: {leagueName} confirmed your {Format.Money(amount)} {rail} payment. {Statement(statementLink)}";

    public static string PaymentRejected(string leagueName, decimal amount, PaymentRail rail, string reason, string statementLink) =>
        $"BallBank: {leagueName} rejected your {Format.Money(amount)} {rail} payment: {reason}. {Statement(statementLink)}";

    /// <param name="amount">Signed: positive raises what the member owes, negative lowers it.</param>
    public static string AdjustmentPosted(string leagueName, decimal amount, string reason, bool refund, string statementLink)
    {
        var what = refund
            ? $"refunded you {Format.Money(amount)} out of the pot"
            : $"{(amount < 0 ? "lowered" : "raised")} your balance by {Format.Money(amount)}";
        return $"BallBank: {leagueName} {what}: {reason}. {Statement(statementLink)}";
    }

    private static string Statement(string link) => $"Your statement: {link}";
}

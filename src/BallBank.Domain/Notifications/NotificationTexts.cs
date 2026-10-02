namespace BallBank.Domain.Notifications;

/// <summary>What a notification is about.</summary>
public static class NotificationKinds
{
    public const string SeasonOpened = "SeasonOpened";
    public const string PaymentConfirmed = "PaymentConfirmed";
    public const string DuesAssessed = "DuesAssessed";
    public const string PaymentAttested = "PaymentAttested";
    public const string PaymentRejected = "PaymentRejected";
    public const string AdjustmentPosted = "AdjustmentPosted";
    public const string Reminder = "Reminder";
    public const string Digest = "Digest";
}

/// <summary>Where a notification is delivered.</summary>
public static class Channels
{
    public const string Discord = "Discord";
    public const string Sms = "Sms";
}

/// <summary>
/// The words of every notification BallBank sends on its own, in one place so the wording is reviewed once.
/// Plain text, so any channel can carry it.
/// </summary>
public static class NotificationTexts
{
    /// <summary>The first thing said through a newly connected channel, to show it works.</summary>
    public static string Hello(string leagueName) =>
        $"Hello from BallBank. {leagueName} is connected to this channel.";

    public static string SeasonOpened(string leagueName, string season, decimal duesAmount, DateOnly dueDate) =>
        $"{leagueName} opened the {season} season. Dues are {Format.Money(duesAmount)}, due {Format.Date(dueDate)}.";

    /// <param name="pot">What the pot holds now, this payment included.</param>
    public static string PaymentConfirmed(string teamName, decimal amount, decimal pot) =>
        $"{teamName} paid {Format.Money(amount)} and the treasurer confirmed it. The pot is now {Format.Money(pot)}.";

    /// <summary>The weekly digest, as lines: who still owes (most first), what the pot holds, and how many payments wait for the treasurer.</summary>
    /// <param name="balances">Every member of the season; those who owe nothing, or are owed, are left out of who still owes.</param>
    public static string Digest(string leagueName, string season, IEnumerable<MemberBalance> balances, decimal pot, int attestationsPending)
    {
        var owing = balances
            .Where(member => member.Balance > 0)
            .OrderByDescending(member => member.Balance)
            .ThenBy(member => member.TeamName, StringComparer.OrdinalIgnoreCase)
            .Select(member => $"{member.TeamName} {Format.Money(member.Balance)}")
            .ToList();

        var waiting = attestationsPending switch
        {
            0 => "No payments are waiting for the treasurer to confirm.",
            1 => "1 payment is waiting for the treasurer to confirm.",
            _ => $"{attestationsPending} payments are waiting for the treasurer to confirm.",
        };

        return string.Join(
            "\n",
            $"Weekly digest for {leagueName}, {season} season.",
            owing.Count == 0 ? "Everyone has paid up." : $"Still owing: {string.Join(", ", owing)}.",
            $"The pot holds {Format.Money(pot)}.",
            waiting);
    }
}

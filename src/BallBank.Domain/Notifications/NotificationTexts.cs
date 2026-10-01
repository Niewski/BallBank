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
}

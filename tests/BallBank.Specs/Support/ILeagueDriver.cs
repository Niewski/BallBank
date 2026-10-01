using BallBank.Domain;
using BallBank.Domain.Notifications;
using BallBank.Domain.Treasury;

namespace BallBank.Specs.Support;

/// <summary>
/// What the treasury steps ask of a league, in league language, whatever runs it: the aggregates in
/// memory (<see cref="InMemoryLeagueDriver"/>) or the API over HTTP (<see cref="HttpLeagueDriver"/>).
/// Members are named by the steps; the treasurer is whoever the driver makes one.
/// </summary>
/// <remarks>
/// A command the league refuses throws <see cref="DomainException"/> with the message the person would
/// read; a command by someone who may not issue it throws <see cref="NotAllowedException"/>.
/// </remarks>
public interface ILeagueDriver
{
    /// <summary>The member id every treasurer's act is recorded under.</summary>
    Guid TreasurerId { get; }

    /// <summary>A league with these members and no season yet.</summary>
    Task OpenLeague(IReadOnlyList<string> members);

    /// <summary>The treasurer opens (or opens again) a season, assessing its dues to every member.</summary>
    Task OpenSeason(string label, decimal duesAmount, DateOnly dueDate);

    /// <summary>Importing the league again adds this member.</summary>
    Task AddMember(string member);

    /// <summary>The treasurer assesses one member of the open season.</summary>
    Task Assess(string member, decimal amount, string memo, DateOnly dueDate);

    /// <summary>The member attests their own payment, unless the treasurer attests it for them.</summary>
    Task Attest(string member, decimal amount, PaymentRail rail, string? reference, bool byTreasurer = false);

    /// <summary>The member attests their latest payment again, under the same attestation id.</summary>
    Task AttestSameAgain(string member);

    Task ConfirmLatest(string member);

    Task RejectLatest(string member, string reason);

    /// <summary>
    /// The treasurer posts an adjustment, unless the member named <paramref name="postedBy"/>, who is no
    /// treasurer, tries to.
    /// </summary>
    Task PostAdjustment(string member, decimal amount, string reason, bool refund = false, string? postedBy = null);

    /// <summary>Positive: the member owes the pot. Negative: the pot owes the member.</summary>
    Task<decimal> Balance(string member);

    Task<int> PendingPayments(string member);

    /// <summary>What the members paid in, less what was refunded.</summary>
    Task<decimal> Pot();

    /// <summary>The instant the league keeps, which a member's quiet hours are read against.</summary>
    DateTimeOffset Now { get; }

    /// <summary>The date the league keeps: the "today" that dues are overdue against.</summary>
    DateOnly Today { get; }

    /// <summary>
    /// The open season's dashboard as its treasurer reads it, once it reflects everything done so far;
    /// or as <paramref name="readBy"/>, who is no treasurer, tries to.
    /// </summary>
    Task<DashboardReading> ReadDashboard(string? readBy = null);

    /// <summary>
    /// The member (or the treasurer, as <paramref name="recordedBy"/>) records the phone number to reach
    /// them at, as they wrote it; <c>null</c> records none.
    /// </summary>
    Task RecordContactDetails(string member, string? phone, string? recordedBy = null);

    /// <summary>
    /// The member opts in to being texted at the number on record, unless the member named
    /// <paramref name="optedInBy"/>, who is not them, tries to for them.
    /// </summary>
    Task OptInToTexts(string member, string? optedInBy = null);

    /// <summary>The member withdraws their consent to be texted.</summary>
    Task OptOutOfTexts(string member);

    /// <summary>The member keeps these hours quiet, on their own clock in this IANA time zone.</summary>
    Task SetQuietHours(string member, int startHour, int endHour, string timeZone);

    /// <summary>What the member has said about being texted, as they read it.</summary>
    Task<TextingReading> ReadTexting(string member);

    /// <summary>
    /// Every text the member has been sent, oldest first, once the league has delivered all it is going to
    /// (as with <see cref="PostedToDiscord"/>). A text held for quiet hours, or skipped, has not been sent.
    /// </summary>
    Task<IReadOnlyList<string>> TextsSentTo(string member);

    /// <summary>Where the member's statement is, as a text names it.</summary>
    Task<string> StatementLink(string member);

    /// <summary>
    /// The treasurer connects the league's Discord channel by its webhook, and says whether confirmed payments
    /// are announced there. Discord is greeted at once.
    /// </summary>
    Task ConnectDiscord(bool announcePayments);

    /// <summary>
    /// Everything Discord has been told, oldest first, once the league has delivered all it is going to
    /// (delivery follows the command that caused it, not the answer to it).
    /// </summary>
    Task<IReadOnlyList<string>> PostedToDiscord();

    /// <summary>Every event of the member's account, oldest first.</summary>
    Task<IReadOnlyList<object>> AccountHistory(string member);

    /// <summary>Every event of the league's seasons and accounts, oldest first.</summary>
    Task<IReadOnlyList<object>> History();
}

/// <summary>What the treasurer reads on the dashboard of a season, delinquents most overdue first.</summary>
public sealed record DashboardReading(
    decimal Assessed,
    decimal Confirmed,
    decimal Refunded,
    decimal Pot,
    decimal Outstanding,
    decimal Owed,
    IReadOnlyList<DelinquentReading> Delinquents);

/// <summary>A member who still owes after the dues were due.</summary>
public sealed record DelinquentReading(string Member, decimal Balance, int DaysOverdue);

/// <summary>Whether, and for which number, a member consented to texts; their quiet hours; and whether they may be texted now.</summary>
public sealed record TextingReading(SmsConsent? Consent, QuietHours QuietHours, bool OptedIn);

/// <summary>Someone tried a command only a treasurer may issue.</summary>
public sealed class NotAllowedException(string member) : Exception($"{member} is not allowed to.")
{
    public string Member { get; } = member;
}

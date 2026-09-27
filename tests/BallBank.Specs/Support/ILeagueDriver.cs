using BallBank.Domain;
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

    /// <summary>Every event of the member's account, oldest first.</summary>
    Task<IReadOnlyList<object>> AccountHistory(string member);

    /// <summary>Every event of the league's seasons and accounts, oldest first.</summary>
    Task<IReadOnlyList<object>> History();
}

/// <summary>Someone tried a command only a treasurer may issue.</summary>
public sealed class NotAllowedException(string member) : Exception($"{member} is not allowed to.")
{
    public string Member { get; } = member;
}

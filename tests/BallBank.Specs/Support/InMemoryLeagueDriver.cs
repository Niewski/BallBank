using BallBank.Domain.Notifications;
using BallBank.Domain.Treasury;

namespace BallBank.Specs.Support;

/// <summary>
/// The league as aggregates in memory, deciding exactly as the endpoints would: members named ahead of
/// any account, so opening a season is what creates their accounts; one history of every event. The
/// default driver, needing neither Docker nor a database.
/// </summary>
public sealed class InMemoryLeagueDriver : ILeagueDriver
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    // The league and teams the HTTP driver imports from the Sleeper fixtures, so both drivers read the same words.
    private const string LeagueName = "Holland Hogs";

    private static readonly Dictionary<string, string> TeamNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Jacob"] = "Hog Wild",
        ["Sam"] = "Sam's Slammers",
    };

    private readonly Dictionary<string, Guid> _memberIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MemberAccount> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, List<object>> _accountHistories = new();
    private readonly Dictionary<string, AttestPayment> _latestAttestation = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, SeasonOpened> _seasons = new();
    private readonly List<object> _history = [];
    private readonly List<string> _postedToDiscord = [];

    private readonly Guid _leagueId = Guid.NewGuid();

    private bool _discordConnected;
    private bool _announcePayments;

    public Guid TreasurerId { get; } = Guid.NewGuid();

    public Task OpenLeague(IReadOnlyList<string> members)
    {
        foreach (var member in members)
        {
            _memberIds[member] = Guid.NewGuid();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The season decides once, and every current member's account is opened and assessed the dues,
    /// all from the deterministic ids alone.
    /// </summary>
    public Task OpenSeason(string label, decimal duesAmount, DateOnly dueDate)
    {
        var seasonId = SeasonIds.SeasonId(_leagueId, label);
        var existing = _seasons.TryGetValue(seasonId, out var alreadyOpened) ? Season.Replay(alreadyOpened) : null;

        var opened = Season.Open(new OpenSeason(seasonId, _leagueId, label, duesAmount, dueDate, TreasurerId), existing, Now);
        if (opened is null)
        {
            return Task.CompletedTask; // already open: a retry, or a second treasurer's click, assesses nobody twice
        }

        _seasons[seasonId] = opened;
        _history.Add(opened);
        Announce(NotificationTexts.SeasonOpened(LeagueName, opened.Label, opened.DuesAmount, opened.DueDate));

        var assessmentId = SeasonIds.DuesAssessmentId(seasonId);
        foreach (var name in _memberIds.Keys)
        {
            var account = OpenAccount(name, opened);
            Record(account, account.Assess(new AssessDues(account.Id, assessmentId, duesAmount, dueDate, "Season dues", TreasurerId), Now));
        }

        return Task.CompletedTask;
    }

    public Task AddMember(string member)
    {
        _memberIds[member] = Guid.NewGuid();
        return Task.CompletedTask;
    }

    /// <summary>An account opened first for a member who joined after the season opened.</summary>
    public Task Assess(string member, decimal amount, string memo, DateOnly dueDate)
    {
        var account = _accounts.TryGetValue(member, out var existing) ? existing : OpenAccount(member, _seasons.Values.Single());
        Record(account, account.Assess(new AssessDues(account.Id, Guid.NewGuid(), amount, dueDate, memo, TreasurerId), Now));
        return Task.CompletedTask;
    }

    public Task Attest(string member, decimal amount, PaymentRail rail, string? reference, bool byTreasurer = false)
    {
        var account = Account(member);
        var command = new AttestPayment(account.Id, Guid.NewGuid(), amount, rail, reference, byTreasurer ? TreasurerId : account.MemberId);
        _latestAttestation[member] = command;
        Record(account, account.Attest(command, Now));
        return Task.CompletedTask;
    }

    public Task AttestSameAgain(string member)
    {
        var account = Account(member);
        Record(account, account.Attest(LatestAttestation(member), Now));
        return Task.CompletedTask;
    }

    public Task ConfirmLatest(string member)
    {
        var account = Account(member);
        var attestation = LatestAttestation(member);
        var confirmed = account.Confirm(new ConfirmPayment(account.Id, attestation.AttestationId, TreasurerId), Now);
        Record(account, confirmed);

        if (confirmed is not null && _announcePayments)
        {
            Announce(NotificationTexts.PaymentConfirmed(TeamNames.GetValueOrDefault(member, member), attestation.Amount, _accounts.Values.Sum(a => a.InThePot)));
        }

        return Task.CompletedTask;
    }

    public Task RejectLatest(string member, string reason)
    {
        var account = Account(member);
        Record(account, account.Reject(new RejectPayment(account.Id, LatestAttestation(member).AttestationId, TreasurerId, reason), Now));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Which member is a treasurer is the league's to say, so the account cannot refuse a member who is
    /// not one; the driver stands in for the endpoint that does (<c>403</c>).
    /// </summary>
    public Task PostAdjustment(string member, decimal amount, string reason, bool refund = false, string? postedBy = null)
    {
        if (postedBy is not null)
        {
            throw new NotAllowedException(postedBy);
        }

        var account = Account(member);
        Record(account, account.PostAdjustment(new PostAdjustment(account.Id, Guid.NewGuid(), amount, reason, refund, TreasurerId), Now));
        return Task.CompletedTask;
    }

    public Task<decimal> Balance(string member) => Task.FromResult(Account(member).Balance);

    public Task<int> PendingPayments(string member) => Task.FromResult(Account(member).PendingAttestations.Count());

    public Task<decimal> Pot() => Task.FromResult(_accounts.Values.Sum(account => account.InThePot));

    public DateOnly Today => DateOnly.FromDateTime(Now.UtcDateTime);

    /// <summary>
    /// What the dashboard adds up to, read off the accounts as they stand. As with <see cref="PostAdjustment"/>,
    /// who is a treasurer is the league's to say, so the driver stands in for the endpoint that answers <c>403</c>.
    /// </summary>
    public Task<DashboardReading> ReadDashboard(string? readBy = null)
    {
        if (readBy is not null)
        {
            throw new NotAllowedException(readBy);
        }

        var accounts = _accounts.Values.ToList();
        var delinquents = _accounts
            .Select(a => (Member: a.Key, Account: a.Value, DaysOverdue: Delinquency.DaysOverdue(a.Value.Balance, EarliestDueDate(a.Value), Today)))
            .Where(owing => owing.DaysOverdue is not null)
            .OrderByDescending(owing => owing.DaysOverdue)
            .ThenByDescending(owing => owing.Account.Balance)
            .Select(owing => new DelinquentReading(owing.Member, owing.Account.Balance, owing.DaysOverdue!.Value))
            .ToList();

        return Task.FromResult(new DashboardReading(
            Assessed: accounts.Sum(a => a.Assessed),
            Confirmed: accounts.Sum(a => a.Confirmed),
            Refunded: accounts.Sum(a => a.Refunded),
            Pot: accounts.Sum(a => a.InThePot),
            Outstanding: accounts.Where(a => a.Balance > 0).Sum(a => a.Balance),
            Owed: -accounts.Where(a => a.Balance < 0).Sum(a => a.Balance),
            Delinquents: delinquents));
    }

    /// <summary>The hello, as the endpoint posts it.</summary>
    public Task ConnectDiscord(bool announcePayments)
    {
        _discordConnected = true;
        _announcePayments = announcePayments;
        Announce(NotificationTexts.Hello(LeagueName));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> PostedToDiscord() => Task.FromResult<IReadOnlyList<string>>([.. _postedToDiscord]);

    public Task<IReadOnlyList<object>> AccountHistory(string member) =>
        Task.FromResult<IReadOnlyList<object>>(_accountHistories[Account(member).Id]);

    public Task<IReadOnlyList<object>> History() => Task.FromResult<IReadOnlyList<object>>(_history);

    // What the announcement handlers would post through a connected channel, in the words of the domain.
    private void Announce(string text)
    {
        if (_discordConnected)
        {
            _postedToDiscord.Add(text);
        }
    }

    private MemberAccount Account(string member) =>
        _accounts.TryGetValue(member, out var account)
            ? account
            : throw new KeyNotFoundException($"No member named '{member}' has an account yet.");

    private MemberAccount OpenAccount(string member, SeasonOpened season)
    {
        var memberId = _memberIds.TryGetValue(member, out var id)
            ? id
            : throw new KeyNotFoundException($"No member named '{member}' in this league.");

        var opened = MemberAccount.Open(new OpenAccount(SeasonIds.AccountId(season.SeasonId, memberId), _leagueId, season.Label, memberId), Now);
        var account = MemberAccount.Replay(opened);
        _accountHistories[account.Id] = [opened];
        _history.Add(opened);
        _accounts[member] = account;
        return account;
    }

    private DateOnly? EarliestDueDate(MemberAccount account) =>
        _accountHistories[account.Id].OfType<DuesAssessed>().Select(assessed => (DateOnly?)assessed.DueDate).Min();

    private AttestPayment LatestAttestation(string member) =>
        _latestAttestation.TryGetValue(member, out var command)
            ? command
            : throw new InvalidOperationException($"{member} has not attested a payment yet.");

    private void Record(MemberAccount account, object? @event)
    {
        if (@event is null)
        {
            return; // idempotent replay: nothing new happened
        }

        account.Evolve(@event);
        _accountHistories[account.Id].Add(@event);
        _history.Add(@event);
    }
}

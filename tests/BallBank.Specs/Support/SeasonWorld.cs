using BallBank.Domain.Treasury;

namespace BallBank.Specs.Support;

/// <summary>
/// In-memory league for the "opening a season" specs: members named ahead of any account, so opening
/// a season is what creates their accounts, exactly as it would over HTTP. Injected into step classes
/// by Reqnroll (one instance per scenario).
/// </summary>
public sealed class SeasonWorld
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly Dictionary<string, Guid> _memberIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MemberAccount> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, SeasonOpened> _seasons = new();
    private readonly List<object> _history = new();

    public Guid LeagueId { get; } = Guid.NewGuid();
    public Guid TreasurerId { get; } = Guid.NewGuid();
    public IReadOnlyList<object> History => _history;

    public void AddMembers(IEnumerable<string> members)
    {
        foreach (var member in members)
        {
            _memberIds[member] = Guid.NewGuid();
        }
    }

    public MemberAccount Account(string member) =>
        _accounts.TryGetValue(member, out var account)
            ? account
            : throw new KeyNotFoundException($"No member named '{member}' has an account yet.");

    /// <summary>
    /// Opens (or re-opens) the season, exactly as the endpoint would: the season decides once, and every
    /// current member's account is opened and assessed the dues, all from the deterministic ids alone.
    /// </summary>
    public void OpenSeason(string label, decimal duesAmount, DateOnly dueDate)
    {
        var seasonId = SeasonIds.SeasonId(LeagueId, label);
        var existing = _seasons.TryGetValue(seasonId, out var alreadyOpened) ? Season.Replay(alreadyOpened) : null;

        var opened = Season.Open(new OpenSeason(seasonId, LeagueId, label, duesAmount, dueDate, TreasurerId), existing, Now);
        if (opened is null)
        {
            return; // already open: a retry, or a second treasurer's click, assesses nobody twice
        }

        _seasons[seasonId] = opened;
        _history.Add(opened);

        var assessmentId = SeasonIds.DuesAssessmentId(seasonId);
        foreach (var (name, memberId) in _memberIds)
        {
            var accountId = SeasonIds.AccountId(seasonId, memberId);
            var accountOpened = MemberAccount.Open(new OpenAccount(accountId, LeagueId, label, memberId), Now);
            var account = MemberAccount.Replay(accountOpened);
            _history.Add(accountOpened);

            var assessed = account.Assess(new AssessDues(accountId, assessmentId, duesAmount, dueDate, "Season dues", TreasurerId), Now)!;
            account.Evolve(assessed);
            _history.Add(assessed);

            _accounts[name] = account;
        }
    }
}

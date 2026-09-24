using BallBank.Domain.Treasury;

namespace BallBank.Specs.Support;

/// <summary>
/// In-memory league for the specs: accounts keyed by member name plus the full event history.
/// Injected into step classes by Reqnroll (one instance per scenario).
/// </summary>
public sealed class LeagueWorld
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly Dictionary<string, MemberAccount> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AttestPayment> _latestAttestation = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<object> _history = new();

    public Guid TreasurerId { get; } = Guid.NewGuid();
    public Guid LeagueId { get; private set; }
    public IReadOnlyList<object> History => _history;
    public decimal Pot => _accounts.Values.Sum(account => account.Confirmed);

    public MemberAccount Account(string member) =>
        _accounts.TryGetValue(member, out var account)
            ? account
            : throw new KeyNotFoundException($"No member named '{member}' in this league.");

    public void OpenLeague(IEnumerable<string> members)
    {
        LeagueId = Guid.NewGuid();

        foreach (var member in members)
        {
            var opened = MemberAccount.Open(new OpenAccount(Guid.NewGuid(), LeagueId, "2026", Guid.NewGuid()), Now);
            _history.Add(opened);
            _accounts[member] = MemberAccount.Replay(opened);
        }
    }

    public void AssessEveryone(decimal amount, DateOnly dueDate)
    {
        foreach (var account in _accounts.Values)
        {
            Record(account, account.Assess(
                new AssessDues(account.Id, Guid.NewGuid(), amount, dueDate, "Season dues", TreasurerId), Now));
        }
    }

    public void Attest(string member, decimal amount, PaymentRail rail, string reference)
    {
        var account = Account(member);
        var command = new AttestPayment(account.Id, Guid.NewGuid(), amount, rail, reference, account.MemberId);
        _latestAttestation[member] = command;
        Record(account, account.Attest(command, Now));
    }

    public void AttestSameAgain(string member)
    {
        var account = Account(member);
        Record(account, account.Attest(LatestAttestation(member), Now));
    }

    public void ConfirmLatest(string member)
    {
        var account = Account(member);
        Record(account, account.Confirm(
            new ConfirmPayment(account.Id, LatestAttestation(member).AttestationId, TreasurerId), Now));
    }

    public void RejectLatest(string member, string reason)
    {
        var account = Account(member);
        Record(account, account.Reject(
            new RejectPayment(account.Id, LatestAttestation(member).AttestationId, TreasurerId, reason), Now));
    }

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
        _history.Add(@event);
    }
}

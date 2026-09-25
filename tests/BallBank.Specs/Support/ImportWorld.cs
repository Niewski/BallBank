using BallBank.Domain;
using BallBank.Domain.Membership;

namespace BallBank.Specs.Support;

/// <summary>
/// In-memory Sleeper and BallBank for the import specs: people keyed by name, each with a Sleeper
/// user id and a sign-in subject; imported leagues; and which Sleeper league backs which league
/// (what the API keeps in <c>SleeperLeagueIndex</c>). Injected into step classes by Reqnroll (one per scenario).
/// </summary>
public sealed class ImportWorld
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly Dictionary<string, string> _sleeperUserIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Guid> _backs = new();
    private readonly List<League> _leagues = [];

    public SleeperLeagueSnapshot? Sleeper { get; private set; }
    public League? League { get; private set; }
    public DomainException? Refusal { get; private set; }

    public string SleeperUserId(string person)
    {
        if (!_sleeperUserIds.TryGetValue(person, out var id))
        {
            id = (100000000000000001L + _sleeperUserIds.Count).ToString();
            _sleeperUserIds[person] = id;
        }

        return id;
    }

    public string NameOf(string? sleeperUserId) =>
        _sleeperUserIds.SingleOrDefault(p => p.Value == sleeperUserId).Key ?? "";

    public static string Subject(string person) => $"test|{person.ToLowerInvariant()}";

    public void SleeperLeague(string name, string season, IEnumerable<(int Roster, string? Owner, string? TeamName, bool Commissioner)> teams)
    {
        var rosters = teams.ToList();
        Sleeper = new SleeperLeagueSnapshot(
            "900000000000000001",
            name,
            season,
            rosters
                .Where(t => t.Owner is not null)
                .Select(t => new SleeperLeagueSnapshot.User(SleeperUserId(t.Owner!), t.Owner, t.Commissioner))
                .ToList(),
            rosters
                .Select(t => new SleeperLeagueSnapshot.Roster(t.Roster, t.Owner is null ? null : SleeperUserId(t.Owner), t.TeamName))
                .ToList());
    }

    /// <summary>Imports the Sleeper league as a new league, recording a refusal instead of throwing.</summary>
    public void Import(string person)
    {
        var snapshot = Sleeper ?? throw new InvalidOperationException("No Sleeper league has been described.");
        var leagueId = Guid.NewGuid();

        try
        {
            var events = League.Import(
                new ImportLeague(
                    leagueId,
                    Guid.NewGuid(),
                    snapshot,
                    snapshot.Rosters.ToDictionary(r => r.RosterId, _ => Guid.NewGuid()),
                    Subject(person),
                    SleeperUserId(person),
                    person,
                    _backs.TryGetValue(snapshot.SleeperLeagueId, out var backed) ? backed : null),
                Now);

            League = League.Replay((LeagueImported)events[0], events.Skip(1));
            _leagues.Add(League);
            _backs[snapshot.SleeperLeagueId] = leagueId;
            Refusal = null;
        }
        catch (DomainException refusal)
        {
            Refusal = refusal;
        }
    }

    public League RequireLeague() =>
        League ?? throw new InvalidOperationException(
            $"No league was imported{(Refusal is null ? "" : $": {Refusal.Message}")}.");
}

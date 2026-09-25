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

    /// <summary>The member each roster was given when the league was first imported.</summary>
    public IReadOnlyDictionary<int, Guid> FirstImportedMemberIds { get; private set; } = new Dictionary<int, Guid>();

    /// <summary>The members the last import added.</summary>
    public IReadOnlyList<MemberAdded> Added { get; private set; } = [];

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

    public void SleeperLeague(string name, string season, IEnumerable<Team> teams) =>
        Sleeper = new SleeperLeagueSnapshot("900000000000000001", name, season, [], []).With(teams, SleeperUserId);

    /// <summary>Teams that joined the Sleeper league after it was described.</summary>
    public void SleeperLeagueGains(IEnumerable<Team> teams) =>
        Sleeper = (Sleeper ?? throw new InvalidOperationException("No Sleeper league has been described.")).With(teams, SleeperUserId);

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
            FirstImportedMemberIds = League.Members.ToDictionary(m => m.SleeperRosterId, m => m.MemberId);
            Added = [.. events.OfType<MemberAdded>()];
            Refusal = null;
        }
        catch (DomainException refusal)
        {
            Refusal = refusal;
        }
    }

    /// <summary>Imports the Sleeper league into the league already imported from it, recording a refusal instead of throwing.</summary>
    public void ImportAgain(string person)
    {
        var snapshot = Sleeper ?? throw new InvalidOperationException("No Sleeper league has been described.");
        var league = RequireLeague();

        try
        {
            var events = league.ImportAgain(
                new ImportLeagueAgain(
                    snapshot,
                    snapshot.Rosters.ToDictionary(r => r.RosterId, _ => Guid.NewGuid()),
                    Subject(person)),
                Now);

            foreach (var @event in events)
            {
                league.Evolve(@event);
            }

            Added = [.. events.OfType<MemberAdded>()];
            Refusal = null;
        }
        catch (DomainException refusal)
        {
            Refusal = refusal;
        }
    }

    /// <summary>The person claims the member for this team, as an invite will let them.</summary>
    public void Claim(string person, string teamName)
    {
        var league = RequireLeague();
        var member = league.Members.Single(m => m.TeamName == teamName);
        league.Evolve(new MemberClaimed(member.MemberId, Subject(person), person, InviteId: Guid.NewGuid(), Now));
    }

    public League RequireLeague() =>
        League ?? throw new InvalidOperationException(
            $"No league was imported{(Refusal is null ? "" : $": {Refusal.Message}")}.");
}

/// <summary>One team as a scenario describes it: its Sleeper roster, owner by name, team name and commissioner flag.</summary>
public sealed record Team(int Roster, string? Owner, string? TeamName, bool Commissioner);

internal static class SleeperLeagueSnapshots
{
    /// <summary>The Sleeper league with these teams, and their owners, added to it.</summary>
    public static SleeperLeagueSnapshot With(this SleeperLeagueSnapshot sleeper, IEnumerable<Team> teams, Func<string, string> sleeperUserId)
    {
        var added = teams.ToList();
        return sleeper with
        {
            Users =
            [
                .. sleeper.Users,
                .. added
                    .Where(t => t.Owner is not null)
                    .Select(t => new SleeperLeagueSnapshot.User(sleeperUserId(t.Owner!), t.Owner, t.Commissioner)),
            ],
            Rosters =
            [
                .. sleeper.Rosters,
                .. added.Select(t => new SleeperLeagueSnapshot.Roster(t.Roster, t.Owner is null ? null : sleeperUserId(t.Owner), t.TeamName)),
            ],
        };
    }
}

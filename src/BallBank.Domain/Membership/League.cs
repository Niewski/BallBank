namespace BallBank.Domain.Membership;

/// <summary>
/// A league: its members, who holds each one, and who keeps the books. Event-sourced like
/// <see cref="Treasury.MemberAccount"/>: one stream per league, whose id is the league id.
/// </summary>
public sealed class League
{
    private readonly Dictionary<Guid, Member> _members = new();

    /// <summary>Stream identity. Public setter so the event store can assign it during aggregation.</summary>
    public Guid Id { get; set; }

    /// <summary>Stream version, set by the event store; used for optimistic concurrency.</summary>
    public int Version { get; set; }

    public string SleeperLeagueId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Season { get; private set; } = string.Empty;

    public IReadOnlyCollection<Member> Members => _members.Values;

    /// <summary>The member this identity holds, or <c>null</c> when it holds none here.</summary>
    public Member? MemberHeldBy(string subject) => _members.Values.FirstOrDefault(m => m.HeldBy == subject);

    // ---------------------------------------------------------------------------------------------
    // Decisions. Pure: read state, return events or throw DomainException. Never mutate here.
    // ---------------------------------------------------------------------------------------------

    /// <summary>The first import of a league.</summary>
    public static IReadOnlyList<object> Import(ImportLeague command, DateTimeOffset now)
    {
        var snapshot = command.Snapshot;

        if (command.SleeperLeagueAlreadyBacks is { } backed && backed != command.LeagueId)
        {
            throw new DomainException(
                $"{snapshot.Name} is already kept in BallBank by another treasurer. Ask them for an invite.");
        }

        var importersRoster = snapshot.RosterOwnedBy(command.ImporterSleeperUserId)
            ?? throw new DomainException(
                $"You do not own a team in {snapshot.Name} on Sleeper, so you cannot import it.");

        if (string.IsNullOrWhiteSpace(command.ImporterDisplayName))
        {
            throw new DomainException("Importing a league needs the name you want to be called in it.");
        }

        List<object> events =
        [
            new LeagueImported(
                command.LeagueId,
                snapshot.SleeperLeagueId,
                snapshot.Name,
                snapshot.Season,
                command.SnapshotId,
                command.ImporterSubject,
                now),
        ];

        events.AddRange(snapshot.Rosters.Select(roster => MemberFor(roster, command, now)));

        // The importer starts out holding their own team and keeping the books.
        var importersMember = command.MemberIds[importersRoster.RosterId];
        events.Add(new MemberClaimed(importersMember, command.ImporterSubject, command.ImporterDisplayName.Trim(), InviteId: null, now));
        events.Add(new TreasurerAppointed(importersMember, command.ImporterSubject, now));

        return events;
    }

    // One member per roster, from its owner; co-owners never appear here because a roster names one owner.
    private static MemberAdded MemberFor(SleeperLeagueSnapshot.Roster roster, ImportLeague command, DateTimeOffset now)
    {
        var owner = command.Snapshot.UserWithId(roster.OwnerUserId);

        return new MemberAdded(
            command.MemberIds[roster.RosterId],
            roster.RosterId,
            roster.OwnerUserId,
            TeamName(roster, owner),
            owner?.DisplayName,
            SuggestedTreasurer: owner?.IsCommissioner == true,
            command.ImporterSubject,
            now);
    }

    // Sleeper shows an unnamed team under its owner's display name; a team nobody owns gets its roster number.
    private static string TeamName(SleeperLeagueSnapshot.Roster roster, SleeperLeagueSnapshot.User? owner) =>
        !string.IsNullOrWhiteSpace(roster.TeamName) ? roster.TeamName.Trim()
        : !string.IsNullOrWhiteSpace(owner?.DisplayName) ? owner.DisplayName.Trim()
        : $"Team {roster.RosterId}";

    // ---------------------------------------------------------------------------------------------
    // Evolution. Not named Apply/Create: see MemberAccount.
    // ---------------------------------------------------------------------------------------------

    private void When(LeagueImported @event)
    {
        Id = @event.LeagueId;
        SleeperLeagueId = @event.SleeperLeagueId;
        Name = @event.Name;
        Season = @event.Season;
    }

    private void When(MemberAdded @event) =>
        _members[@event.MemberId] = new Member(
            @event.MemberId,
            @event.SleeperRosterId,
            @event.SleeperUserId,
            @event.TeamName,
            @event.SleeperDisplayName,
            @event.SuggestedTreasurer);

    private void When(MemberClaimed @event) =>
        _members[@event.MemberId] = _members[@event.MemberId] with
        {
            HeldBy = @event.Subject,
            HolderDisplayName = @event.DisplayName,
        };

    private void When(TreasurerAppointed @event) =>
        _members[@event.MemberId] = _members[@event.MemberId] with { IsTreasurer = true };

    /// <summary>Applies any event of this stream after the first.</summary>
    public void Evolve(object @event)
    {
        switch (@event)
        {
            case LeagueImported e:
                When(e);
                break;
            case MemberAdded e:
                When(e);
                break;
            case MemberClaimed e:
                When(e);
                break;
            case TreasurerAppointed e:
                When(e);
                break;
            default:
                throw new InvalidOperationException($"League does not know the event {@event.GetType().Name}.");
        }
    }

    /// <summary>Rebuilds a league from its history, starting with the event that imported it.</summary>
    public static League Replay(LeagueImported imported, params IEnumerable<object> history)
    {
        var league = new League();
        league.When(imported);
        foreach (var @event in history)
        {
            league.Evolve(@event);
        }

        return league;
    }
}

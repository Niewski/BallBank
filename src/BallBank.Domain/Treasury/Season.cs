namespace BallBank.Domain.Treasury;

/// <summary>
/// One season of a league: its label, dues and due date. Event-sourced like <see cref="MemberAccount"/>:
/// one stream per league × season, whose id is a deterministic id of the league and the label
/// (<see cref="SeasonIds"/>), so opening a season twice lands on the same stream instead of a new one.
/// </summary>
public sealed class Season
{
    /// <summary>Stream identity. Public setter so the event store can assign it during aggregation.</summary>
    public Guid Id { get; set; }

    /// <summary>Stream version, set by the event store; used for optimistic concurrency.</summary>
    public int Version { get; set; }

    public Guid LeagueId { get; private set; }
    public string Label { get; private set; } = string.Empty;
    public decimal DuesAmount { get; private set; }
    public DateOnly DueDate { get; private set; }
    public Guid OpenedBy { get; private set; }
    public DateTimeOffset OpenedAt { get; private set; }

    // ---------------------------------------------------------------------------------------------
    // Decisions. Pure: read state, return an event or throw DomainException. Never mutate here.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The first opening of a season. Returns <c>null</c> when <paramref name="existing"/> is already
    /// this season, opened before: a retry, or a second treasurer's click, cannot double-assess.
    /// </summary>
    public static SeasonOpened? Open(OpenSeason command, Season? existing, DateTimeOffset now)
    {
        if (existing is not null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(command.Label))
        {
            throw new DomainException("A season needs a label.");
        }

        if (command.DuesAmount <= 0)
        {
            throw new DomainException("Season dues must be a positive amount.");
        }

        if (command.DueDate is not { } dueDate)
        {
            throw new DomainException("A season needs a due date.");
        }

        return new SeasonOpened(command.SeasonId, command.LeagueId, command.Label.Trim(), command.DuesAmount, dueDate, command.OpenedBy, now);
    }

    // ---------------------------------------------------------------------------------------------
    // Evolution. Not named Apply/Create: Marten 9 claims those names by convention (see MemberAccount).
    // ---------------------------------------------------------------------------------------------

    private void When(SeasonOpened @event)
    {
        Id = @event.SeasonId;
        LeagueId = @event.LeagueId;
        Label = @event.Label;
        DuesAmount = @event.DuesAmount;
        DueDate = @event.DueDate;
        OpenedBy = @event.OpenedBy;
        OpenedAt = @event.OpenedAt;
    }

    /// <summary>Applies any event of this stream after the first.</summary>
    public void Evolve(object @event)
    {
        switch (@event)
        {
            case SeasonOpened e:
                When(e);
                break;
            default:
                throw new InvalidOperationException($"Season does not know the event {@event.GetType().Name}.");
        }
    }

    /// <summary>Rebuilds a season from its history, starting with the event that opened it.</summary>
    public static Season Replay(SeasonOpened opened, params object[] history)
    {
        var season = new Season();
        season.When(opened);
        foreach (var @event in history)
        {
            season.Evolve(@event);
        }

        return season;
    }
}

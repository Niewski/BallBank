namespace BallBank.Domain.Membership;

/// <summary>
/// A league: its members, who holds each one, and who keeps the books. Event-sourced like
/// <see cref="Treasury.MemberAccount"/>: one stream per league, whose id is the league id.
/// </summary>
public sealed class League
{
    private readonly Dictionary<Guid, Member> _members = new();
    private readonly Dictionary<Guid, Invite> _invites = new();

    // The latest invite issued for each member: the only one of theirs that can be valid.
    private readonly Dictionary<Guid, Guid> _latestInvites = new();

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

    /// <summary>The invite issued with this id, valid or not, or <c>null</c> when none was.</summary>
    public Invite? InviteWithId(Guid inviteId) => _invites.GetValueOrDefault(inviteId);

    /// <summary>
    /// Whether this invite can be used at <paramref name="now"/>: it was issued, no newer invite for
    /// its member has voided it, and it has not expired.
    /// </summary>
    public bool IsInviteValid(Guid inviteId, DateTimeOffset now) => StatusOfInvite(inviteId, now) == InviteStatus.Valid;

    /// <summary>Whether this invite can be used at <paramref name="now"/>, and if not, why not.</summary>
    public InviteStatus StatusOfInvite(Guid inviteId, DateTimeOffset now) =>
        !_invites.TryGetValue(inviteId, out var invite) ? InviteStatus.NotIssued
        : _latestInvites[invite.MemberId] != inviteId ? InviteStatus.Replaced
        : now >= invite.ExpiresAt ? InviteStatus.Expired
        : InviteStatus.Valid;

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

        events.AddRange(snapshot.Rosters.Select(roster =>
            MemberFor(roster, snapshot, command.MemberIds[roster.RosterId], command.ImporterSubject, now)));

        // The importer starts out holding their own team and keeping the books.
        var importersMember = command.MemberIds[importersRoster.RosterId];
        events.Add(new MemberClaimed(importersMember, command.ImporterSubject, command.ImporterDisplayName.Trim(), InviteId: null, now));
        events.Add(new TreasurerAppointed(importersMember, command.ImporterSubject, now));

        return events;
    }

    /// <summary>
    /// Importing the league again: each roster the league does not know yet becomes a member, matched
    /// by Sleeper roster id. Members already known, and who holds them, are left alone.
    /// </summary>
    public IReadOnlyList<object> ImportAgain(ImportLeagueAgain command, DateTimeOffset now)
    {
        var snapshot = command.Snapshot;

        if (snapshot.SleeperLeagueId != SleeperLeagueId)
        {
            throw new DomainException($"{Name} is kept from a different Sleeper league than {snapshot.Name}.");
        }

        if (MemberHeldBy(command.ImporterSubject) is not { IsTreasurer: true })
        {
            throw new DomainException($"Only a treasurer of {Name} can import it again.");
        }

        return snapshot.Rosters
            .Where(roster => _members.Values.All(m => m.SleeperRosterId != roster.RosterId))
            .Select(roster => MemberFor(roster, snapshot, command.MemberIds[roster.RosterId], command.ImporterSubject, now))
            .ToList<object>();
    }

    /// <summary>
    /// The contact details to keep for a member. A treasurer changes anyone's, claimed or not; a member
    /// changes their own. Decided here but written as a document, never an event (ADR-0012).
    /// </summary>
    public ContactDetails RecordContactDetails(RecordContactDetails command)
    {
        if (!_members.TryGetValue(command.MemberId, out var member))
        {
            throw new DomainException($"{Name} has no such member.");
        }

        if (MemberHeldBy(command.EditorSubject) is not { } editor
            || (!editor.IsTreasurer && editor.MemberId != member.MemberId))
        {
            throw new DomainException(
                $"Only a treasurer of {Name}, or whoever holds {member.TeamName}, can change its contact details.");
        }

        var details = ContactDetails.From(command.Email, command.Phone, command.DiscordUsername);

        // Before a claim there may be nothing to go on; once someone holds the member, BallBank can reach them.
        if (member.IsClaimed && details.Email is null)
        {
            throw new DomainException($"{member.TeamName} is claimed, so its contact details need an email.");
        }

        return details;
    }

    /// <summary>How long an invite can be used after it is issued.</summary>
    public static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(14);

    /// <summary>An invite for an unclaimed member, by a treasurer. Returns <c>null</c> when this invite id was already issued.</summary>
    public InviteIssued? IssueInvite(IssueInvite command, DateTimeOffset now)
    {
        if (!_members.TryGetValue(command.MemberId, out var member))
        {
            throw new DomainException($"{Name} has no such member.");
        }

        if (MemberHeldBy(command.IssuerSubject) is not { IsTreasurer: true })
        {
            throw new DomainException($"Only a treasurer of {Name} can invite someone to claim a member.");
        }

        // Checked before the claim below: an invite that was issued, and perhaps used since, stays issued.
        if (_invites.TryGetValue(command.InviteId, out var issued))
        {
            return issued.MemberId == command.MemberId
                ? null
                : throw new DomainException("That invite was issued for another member.");
        }

        // A new invite must not take a member from whoever holds it; the treasurer revokes the claim first.
        if (member.IsClaimed)
        {
            throw new DomainException(
                $"{member.TeamName} is already claimed by {member.HolderDisplayName}. Revoke that claim before inviting someone else.");
        }

        return new InviteIssued(command.InviteId, command.MemberId, command.IssuerSubject, now + InviteLifetime, now);
    }

    /// <summary>
    /// The identity becomes the member through a valid invite for it. Returns <c>null</c> when that
    /// identity already holds the member, so a double click or a retry claims once.
    /// </summary>
    public MemberClaimed? ClaimMember(ClaimMember command, DateTimeOffset now)
    {
        if (!_members.TryGetValue(command.MemberId, out var member))
        {
            throw new DomainException($"{Name} has no such member.");
        }

        // Checked before the invite: a retry after the invite expired still finds the claim it made.
        if (member.HeldBy == command.ClaimantSubject)
        {
            return null;
        }

        if (InviteWithId(command.InviteId)?.MemberId != command.MemberId)
        {
            throw new DomainException($"That is not an invite to claim {member.TeamName}.");
        }

        // An invite that can no longer be used is refused before anything about who holds what, so a
        // refusal is about the invite exactly when the invite is not valid.
        switch (StatusOfInvite(command.InviteId, now))
        {
            case InviteStatus.Replaced:
                throw new DomainException("This invite was replaced by a newer one. Use the latest link the treasurer sent.");
            case InviteStatus.Expired:
                throw new DomainException("This invite has expired. Ask the treasurer for a new one.");
        }

        // A member is held by at most one identity (ADR-0010).
        if (member.IsClaimed)
        {
            throw new DomainException(
                $"{member.TeamName} is already claimed by someone else. If that is wrong, ask the treasurer.");
        }

        // An identity holds at most one member per league (ADR-0010).
        if (MemberHeldBy(command.ClaimantSubject) is { } held)
        {
            throw new DomainException(
                $"You already hold {held.TeamName} in {Name}, and one person holds one member per league.");
        }

        if (string.IsNullOrWhiteSpace(command.DisplayName))
        {
            throw new DomainException("Claiming a member needs the name you want to be called in the league.");
        }

        return new MemberClaimed(command.MemberId, command.ClaimantSubject, command.DisplayName.Trim(), command.InviteId, now);
    }

    /// <summary>A claimed member becomes another treasurer, by a treasurer's hand.</summary>
    public TreasurerAppointed? AppointTreasurer(AppointTreasurer command, DateTimeOffset now)
    {
        if (!_members.TryGetValue(command.MemberId, out var member))
        {
            throw new DomainException($"{Name} has no such member.");
        }

        // The API asks the caller's memberships first; the league, the source of truth, asks again (ADR-0011).
        if (MemberHeldBy(command.AppointerSubject) is not { IsTreasurer: true })
        {
            throw new DomainException($"Only a treasurer of {Name} can appoint another treasurer.");
        }

        // There is one Treasurer role (ADR-0010): a treasurer appointed again is still just a treasurer.
        if (member.IsTreasurer)
        {
            return null;
        }

        // A role belongs to one person, so there must be a person to give it to.
        if (!member.IsClaimed)
        {
            throw new DomainException(
                $"{member.TeamName} is not claimed yet, and only a claimed member can be a treasurer. Invite them first.");
        }

        return new TreasurerAppointed(command.MemberId, command.AppointerSubject, now);
    }

    /// <summary>
    /// A treasurer revokes a subject's claim to a member, with a reason, so it can be claimed again.
    /// The member keeps its account and history (ADR-0010). Returns <c>null</c> when that subject does
    /// not hold the member: they may never have claimed it, or their claim may already be revoked, or
    /// claimed since by someone else.
    /// </summary>
    public MemberClaimRevoked? RevokeClaim(RevokeClaim command, DateTimeOffset now)
    {
        if (!_members.TryGetValue(command.MemberId, out var member))
        {
            throw new DomainException($"{Name} has no such member.");
        }

        // The API asks the caller's memberships first; the league, the source of truth, asks again (ADR-0011).
        if (MemberHeldBy(command.RevokerSubject) is not { IsTreasurer: true })
        {
            throw new DomainException($"Only a treasurer of {Name} can revoke a claim.");
        }

        if (member.HeldBy != command.Subject)
        {
            return null;
        }

        // A league keeps at least one treasurer; appoint another before revoking the only one's claim.
        if (member.IsTreasurer && _members.Values.Count(m => m.IsTreasurer) == 1)
        {
            throw new DomainException(
                $"{member.TeamName} is the only treasurer of {Name}. Appoint another treasurer before revoking this claim.");
        }

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            throw new DomainException("Revoking a claim needs a reason.");
        }

        return new MemberClaimRevoked(command.MemberId, command.Subject, command.RevokerSubject, command.Reason.Trim(), now);
    }

    // One member per roster, from its owner; co-owners never appear here because a roster names one owner.
    private static MemberAdded MemberFor(
        SleeperLeagueSnapshot.Roster roster,
        SleeperLeagueSnapshot snapshot,
        Guid memberId,
        string addedBy,
        DateTimeOffset now)
    {
        var owner = snapshot.UserWithId(roster.OwnerUserId);

        return new MemberAdded(
            memberId,
            roster.RosterId,
            roster.OwnerUserId,
            TeamName(roster, owner),
            owner?.DisplayName,
            SuggestedTreasurer: owner?.IsCommissioner == true,
            addedBy,
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

    private void When(InviteIssued @event)
    {
        _invites[@event.InviteId] = new Invite(@event.InviteId, @event.MemberId, @event.ExpiresAt);
        _latestInvites[@event.MemberId] = @event.InviteId;
    }

    private void When(MemberClaimRevoked @event) =>
        _members[@event.MemberId] = _members[@event.MemberId] with
        {
            HeldBy = null,
            HolderDisplayName = null,
            IsTreasurer = false,
        };

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
            case InviteIssued e:
                When(e);
                break;
            case MemberClaimRevoked e:
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

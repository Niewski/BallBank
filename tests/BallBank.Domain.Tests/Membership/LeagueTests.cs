using BallBank.Domain.Membership;

namespace BallBank.Domain.Tests.Membership;

public class LeagueTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private const string Jacob = "100000000000000001";
    private const string Sam = "100000000000000002";
    private const string Priya = "100000000000000003";
    private const string JacobsSubject = "test|jacob";

    private static readonly SleeperLeagueSnapshot HollandHogs = new(
        "900000000000000001",
        "Holland Hogs",
        "2026",
        [
            new SleeperLeagueSnapshot.User(Jacob, "Jacob", IsCommissioner: true),
            new SleeperLeagueSnapshot.User(Sam, "Sam", IsCommissioner: false),
            new SleeperLeagueSnapshot.User(Priya, "Priya", IsCommissioner: false),
        ],
        [
            new SleeperLeagueSnapshot.Roster(1, Jacob, "Hog Wild"),
            new SleeperLeagueSnapshot.Roster(2, Sam, "Sam's Slammers"),
            new SleeperLeagueSnapshot.Roster(3, Priya, TeamName: null),
            new SleeperLeagueSnapshot.Roster(4, OwnerUserId: null, TeamName: null),
        ]);

    private static ImportLeague Import(
        SleeperLeagueSnapshot? snapshot = null,
        string importerSleeperUserId = Jacob,
        Guid? leagueId = null,
        Guid? sleeperLeagueAlreadyBacks = null)
    {
        snapshot ??= HollandHogs;
        return new ImportLeague(
            leagueId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            snapshot,
            snapshot.Rosters.ToDictionary(r => r.RosterId, _ => Guid.NewGuid()),
            JacobsSubject,
            importerSleeperUserId,
            "Jacob",
            sleeperLeagueAlreadyBacks);
    }

    [Fact]
    public void Importing_records_the_league_with_its_season_and_snapshot()
    {
        var command = Import();

        var events = League.Import(command, Now);

        events[0].ShouldBe(new LeagueImported(
            command.LeagueId,
            "900000000000000001",
            "Holland Hogs",
            "2026",
            command.SnapshotId,
            JacobsSubject,
            Now));
    }

    [Fact]
    public void Every_roster_becomes_a_member_including_the_one_nobody_owns()
    {
        var command = Import();

        var added = League.Import(command, Now).OfType<MemberAdded>().ToList();

        added.ShouldBe(
        [
            new MemberAdded(command.MemberIds[1], 1, Jacob, "Hog Wild", "Jacob", SuggestedTreasurer: true, JacobsSubject, Now),
            new MemberAdded(command.MemberIds[2], 2, Sam, "Sam's Slammers", "Sam", SuggestedTreasurer: false, JacobsSubject, Now),
            new MemberAdded(command.MemberIds[3], 3, Priya, "Priya", "Priya", SuggestedTreasurer: false, JacobsSubject, Now),
            new MemberAdded(command.MemberIds[4], 4, null, "Team 4", null, SuggestedTreasurer: false, JacobsSubject, Now),
        ]);
    }

    [Fact]
    public void The_importer_claims_their_own_member_and_becomes_a_treasurer()
    {
        var command = Import();

        var events = League.Import(command, Now);

        var jacobsMember = command.MemberIds[1];
        events.OfType<MemberClaimed>().ShouldHaveSingleItem()
            .ShouldBe(new MemberClaimed(jacobsMember, JacobsSubject, "Jacob", InviteId: null, Now));
        events.OfType<TreasurerAppointed>().ShouldHaveSingleItem()
            .ShouldBe(new TreasurerAppointed(jacobsMember, JacobsSubject, Now));
    }

    [Fact]
    public void An_imported_league_has_its_members_with_the_importer_holding_theirs()
    {
        var command = Import();
        var events = League.Import(command, Now);

        var league = League.Replay((LeagueImported)events[0], events.Skip(1));

        league.Name.ShouldBe("Holland Hogs");
        league.Season.ShouldBe("2026");
        league.Members.Count.ShouldBe(4);
        var jacob = league.MemberHeldBy(JacobsSubject);
        jacob.ShouldNotBeNull();
        jacob.MemberId.ShouldBe(command.MemberIds[1]);
        jacob.HolderDisplayName.ShouldBe("Jacob");
        jacob.IsTreasurer.ShouldBeTrue();
        league.Members.Where(m => m.MemberId != jacob.MemberId).ShouldAllBe(m => !m.IsClaimed && !m.IsTreasurer);
    }

    [Fact]
    public void An_importer_who_owns_no_roster_is_refused()
    {
        var dana = "100000000000000009";

        Should.Throw<DomainException>(() =>
        {
            League.Import(Import(importerSleeperUserId: dana), Now);
        });
    }

    [Fact]
    public void A_Sleeper_league_that_backs_another_league_cannot_be_imported()
    {
        Should.Throw<DomainException>(() =>
        {
            League.Import(Import(sleeperLeagueAlreadyBacks: Guid.NewGuid()), Now);
        });
    }

    [Fact]
    public void An_importer_needs_a_display_name()
    {
        Should.Throw<DomainException>(() =>
        {
            League.Import(Import() with { ImporterDisplayName = " " }, Now);
        });
    }

    [Fact]
    public void A_Sleeper_co_owner_does_not_become_a_member()
    {
        // Alex co-owns Sam's team on Sleeper. Co-owners are not members (ADR-0010).
        var withCoOwner = HollandHogs with
        {
            Users = [.. HollandHogs.Users, new SleeperLeagueSnapshot.User("100000000000000004", "Alex", IsCommissioner: true)],
        };

        var added = League.Import(Import(withCoOwner), Now).OfType<MemberAdded>().ToList();

        added.Count.ShouldBe(4);
        added.ShouldNotContain(m => m.SleeperUserId == "100000000000000004");
        added.Where(m => m.SuggestedTreasurer).ShouldHaveSingleItem().SleeperUserId.ShouldBe(Jacob);
    }

    // ---------------------------------------------------------------------------------------------
    // Importing again
    // ---------------------------------------------------------------------------------------------

    private const string Dana = "100000000000000005";

    // Dana joined the Sleeper league after it was imported, on a fifth team.
    private static readonly SleeperLeagueSnapshot HollandHogsWithDana = HollandHogs with
    {
        Users = [.. HollandHogs.Users, new SleeperLeagueSnapshot.User(Dana, "Dana", IsCommissioner: false)],
        Rosters = [.. HollandHogs.Rosters, new SleeperLeagueSnapshot.Roster(5, Dana, "Dana's Dynasty")],
    };

    private static League Imported()
    {
        var events = League.Import(Import(), Now);
        return League.Replay((LeagueImported)events[0], events.Skip(1));
    }

    private static ImportLeagueAgain ImportAgain(SleeperLeagueSnapshot? snapshot = null, string importerSubject = JacobsSubject)
    {
        snapshot ??= HollandHogs;
        return new ImportLeagueAgain(
            snapshot,
            snapshot.Rosters.ToDictionary(r => r.RosterId, _ => Guid.NewGuid()),
            importerSubject);
    }

    [Fact]
    public void Importing_again_after_a_team_joins_adds_exactly_that_member()
    {
        var league = Imported();
        var command = ImportAgain(HollandHogsWithDana);

        var events = league.ImportAgain(command, Now);

        events.ShouldHaveSingleItem().ShouldBe(
            new MemberAdded(command.MemberIds[5], 5, Dana, "Dana's Dynasty", "Dana", SuggestedTreasurer: false, JacobsSubject, Now));
    }

    [Fact]
    public void Importing_again_with_no_new_teams_adds_nothing()
    {
        var league = Imported();

        league.ImportAgain(ImportAgain(), Now).ShouldBeEmpty();
    }

    [Fact]
    public void Importing_again_leaves_known_members_alone_even_when_Sleeper_has_changed_them()
    {
        // Sam renamed their team and Priya handed hers to Dana. Owner changes are not imported (ADR-0010).
        var changed = HollandHogs with
        {
            Users = [.. HollandHogs.Users, new SleeperLeagueSnapshot.User(Dana, "Dana", IsCommissioner: false)],
            Rosters =
            [
                HollandHogs.Rosters[0],
                HollandHogs.Rosters[1] with { TeamName = "Slam Dunkers" },
                HollandHogs.Rosters[2] with { OwnerUserId = Dana },
                HollandHogs.Rosters[3],
            ],
        };
        var league = Imported();

        league.ImportAgain(ImportAgain(changed), Now).ShouldBeEmpty();
    }

    [Fact]
    public void A_member_who_is_not_a_treasurer_cannot_import_again()
    {
        var league = Imported();
        var sams = league.Members.Single(m => m.SleeperRosterId == 2).MemberId;
        league.Evolve(new MemberClaimed(sams, "test|sam", "Sam", InviteId: Guid.NewGuid(), Now));

        Should.Throw<DomainException>(() =>
        {
            league.ImportAgain(ImportAgain(HollandHogsWithDana, importerSubject: "test|sam"), Now);
        });
    }

    [Fact]
    public void A_different_Sleeper_league_cannot_be_imported_into_a_league()
    {
        var league = Imported();
        var anotherLeague = HollandHogsWithDana with { SleeperLeagueId = "900000000000000002" };

        Should.Throw<DomainException>(() =>
        {
            league.ImportAgain(ImportAgain(anotherLeague), Now);
        });
    }

    // ---------------------------------------------------------------------------------------------
    // Invites
    // ---------------------------------------------------------------------------------------------

    private static Guid MemberFor(League league, int rosterId) =>
        league.Members.Single(m => m.SleeperRosterId == rosterId).MemberId;

    private static IssueInvite IssueInviteFor(Guid memberId, string issuerSubject = JacobsSubject) =>
        new(Guid.NewGuid(), memberId, issuerSubject);

    [Fact]
    public void An_invite_is_issued_for_a_member_and_expires_14_days_later()
    {
        var league = Imported();
        var priyas = MemberFor(league, 3);
        var command = IssueInviteFor(priyas);

        var issued = league.IssueInvite(command, Now);

        issued.ShouldBe(new InviteIssued(
            command.InviteId,
            priyas,
            JacobsSubject,
            new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero),
            Now));
    }

    [Fact]
    public void A_newer_invite_for_a_member_voids_the_earlier_one()
    {
        var league = Imported();
        var priyas = MemberFor(league, 3);
        var first = IssueInviteFor(priyas);
        league.Evolve(league.IssueInvite(first, Now)!);

        var second = IssueInviteFor(priyas);
        league.Evolve(league.IssueInvite(second, Now.AddDays(1))!);

        league.IsInviteValid(first.InviteId, Now.AddDays(1)).ShouldBeFalse();
        league.IsInviteValid(second.InviteId, Now.AddDays(1)).ShouldBeTrue();
    }

    [Fact]
    public void An_invite_for_one_member_leaves_another_members_invite_valid()
    {
        var league = Imported();
        var priyas = IssueInviteFor(MemberFor(league, 3));
        var team4s = IssueInviteFor(MemberFor(league, 4));

        league.Evolve(league.IssueInvite(priyas, Now)!);
        league.Evolve(league.IssueInvite(team4s, Now)!);

        league.IsInviteValid(priyas.InviteId, Now).ShouldBeTrue();
        league.IsInviteValid(team4s.InviteId, Now).ShouldBeTrue();
    }

    [Fact]
    public void An_invite_is_valid_until_14_days_after_it_was_issued()
    {
        var league = Imported();
        var command = IssueInviteFor(MemberFor(league, 3));
        league.Evolve(league.IssueInvite(command, Now)!);

        league.IsInviteValid(command.InviteId, new DateTimeOffset(2026, 10, 8, 11, 59, 59, TimeSpan.Zero)).ShouldBeTrue();
        league.IsInviteValid(command.InviteId, new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero)).ShouldBeFalse();
    }

    [Fact]
    public void An_invite_never_issued_is_not_valid()
    {
        Imported().IsInviteValid(Guid.NewGuid(), Now).ShouldBeFalse();
    }

    [Fact]
    public void Issuing_an_invite_for_a_claimed_member_is_refused()
    {
        // The treasurer revokes Sam's claim first if they mean to hand the team to someone else.
        var league = Imported();
        var sams = MemberFor(league, 2);
        league.Evolve(new MemberClaimed(sams, "test|sam", "Sam", InviteId: Guid.NewGuid(), Now));

        Should.Throw<DomainException>(() =>
        {
            league.IssueInvite(IssueInviteFor(sams), Now);
        });
    }

    [Fact]
    public void A_member_who_is_not_a_treasurer_cannot_issue_an_invite()
    {
        var league = Imported();
        league.Evolve(new MemberClaimed(MemberFor(league, 2), "test|sam", "Sam", InviteId: Guid.NewGuid(), Now));

        Should.Throw<DomainException>(() =>
        {
            league.IssueInvite(IssueInviteFor(MemberFor(league, 3), issuerSubject: "test|sam"), Now);
        });
    }

    [Fact]
    public void Someone_who_holds_no_member_cannot_issue_an_invite()
    {
        var league = Imported();

        Should.Throw<DomainException>(() =>
        {
            league.IssueInvite(IssueInviteFor(MemberFor(league, 3), issuerSubject: "test|stranger"), Now);
        });
    }

    [Fact]
    public void An_invite_for_a_member_the_league_does_not_have_is_refused()
    {
        Should.Throw<DomainException>(() =>
        {
            Imported().IssueInvite(IssueInviteFor(Guid.NewGuid()), Now);
        });
    }

    [Fact]
    public void Issuing_the_same_invite_again_is_a_no_op()
    {
        var league = Imported();
        var command = IssueInviteFor(MemberFor(league, 3));
        league.Evolve(league.IssueInvite(command, Now)!);

        league.IssueInvite(command, Now.AddMinutes(1)).ShouldBeNull();
    }

    [Fact]
    public void Issuing_a_voided_invite_again_does_not_bring_it_back()
    {
        var league = Imported();
        var priyas = MemberFor(league, 3);
        var first = IssueInviteFor(priyas);
        league.Evolve(league.IssueInvite(first, Now)!);
        league.Evolve(league.IssueInvite(IssueInviteFor(priyas), Now)!);

        league.IssueInvite(first, Now).ShouldBeNull();
    }

    [Fact]
    public void An_invite_id_already_issued_for_another_member_is_refused()
    {
        var league = Imported();
        var command = IssueInviteFor(MemberFor(league, 3));
        league.Evolve(league.IssueInvite(command, Now)!);

        Should.Throw<DomainException>(() =>
        {
            league.IssueInvite(command with { MemberId = MemberFor(league, 4) }, Now);
        });
    }

    // ---------------------------------------------------------------------------------------------
    // Claims
    // ---------------------------------------------------------------------------------------------

    private const string PriyasSubject = "test|priya";

    /// <summary>The league with an invite issued at <see cref="Now"/> for the member on this roster.</summary>
    private static (League League, Guid MemberId, Guid InviteId) Invited(int rosterId = 3)
    {
        var league = Imported();
        var memberId = MemberFor(league, rosterId);
        var command = IssueInviteFor(memberId);
        league.Evolve(league.IssueInvite(command, Now)!);
        return (league, memberId, command.InviteId);
    }

    [Fact]
    public void An_invited_person_claims_the_member_with_a_valid_invite()
    {
        var (league, priyas, inviteId) = Invited();

        var claimed = league.ClaimMember(new ClaimMember(priyas, inviteId, PriyasSubject, " Priya "), Now.AddDays(1));

        claimed.ShouldBe(new MemberClaimed(priyas, PriyasSubject, "Priya", inviteId, Now.AddDays(1)));
    }

    [Fact]
    public void An_invite_that_has_expired_cannot_be_used_to_claim()
    {
        var (league, priyas, inviteId) = Invited();

        Should.Throw<DomainException>(() =>
        {
            league.ClaimMember(new ClaimMember(priyas, inviteId, PriyasSubject, "Priya"), Now + League.InviteLifetime);
        }).Message.ShouldBe("This invite has expired. Ask the treasurer for a new one.");
    }

    [Fact]
    public void An_invite_a_newer_one_replaced_cannot_be_used_to_claim()
    {
        var (league, priyas, first) = Invited();
        league.Evolve(league.IssueInvite(IssueInviteFor(priyas), Now.AddDays(1))!);

        Should.Throw<DomainException>(() =>
        {
            league.ClaimMember(new ClaimMember(priyas, first, PriyasSubject, "Priya"), Now.AddDays(2));
        }).Message.ShouldBe("This invite was replaced by a newer one. Use the latest link the treasurer sent.");
    }

    [Fact]
    public void A_member_someone_holds_cannot_be_claimed_by_anyone_else()
    {
        // Priya forwarded her link, and Dana opened it after she had claimed.
        var (league, priyas, inviteId) = Invited();
        league.Evolve(league.ClaimMember(new ClaimMember(priyas, inviteId, PriyasSubject, "Priya"), Now)!);

        Should.Throw<DomainException>(() =>
        {
            league.ClaimMember(new ClaimMember(priyas, inviteId, "test|dana", "Dana"), Now.AddHours(1));
        }).Message.ShouldBe("Priya is already claimed by someone else. If that is wrong, ask the treasurer.");
    }

    [Fact]
    public void Someone_who_already_holds_a_member_cannot_claim_another()
    {
        // Jacob holds Hog Wild since the import, and opens the invite he made for Priya.
        var (league, priyas, inviteId) = Invited();

        Should.Throw<DomainException>(() =>
        {
            league.ClaimMember(new ClaimMember(priyas, inviteId, JacobsSubject, "Jacob"), Now);
        }).Message.ShouldBe("You already hold Hog Wild in Holland Hogs, and one person holds one member per league.");
    }

    [Fact]
    public void Claiming_again_by_whoever_holds_the_member_is_a_no_op()
    {
        var (league, priyas, inviteId) = Invited();
        var command = new ClaimMember(priyas, inviteId, PriyasSubject, "Priya");
        league.Evolve(league.ClaimMember(command, Now)!);

        league.ClaimMember(command, Now.AddMinutes(1)).ShouldBeNull();
        league.ClaimMember(command, Now + League.InviteLifetime).ShouldBeNull();
    }

    [Fact]
    public void An_invite_never_issued_cannot_be_used_to_claim()
    {
        var (league, priyas, _) = Invited();

        Should.Throw<DomainException>(() =>
        {
            league.ClaimMember(new ClaimMember(priyas, Guid.NewGuid(), PriyasSubject, "Priya"), Now);
        });
    }

    [Fact]
    public void An_invite_claims_only_the_member_it_was_issued_for()
    {
        var (league, _, inviteId) = Invited();

        Should.Throw<DomainException>(() =>
        {
            league.ClaimMember(new ClaimMember(MemberFor(league, 4), inviteId, PriyasSubject, "Priya"), Now);
        });
    }

    [Fact]
    public void A_claim_needs_a_display_name()
    {
        var (league, priyas, inviteId) = Invited();

        Should.Throw<DomainException>(() =>
        {
            league.ClaimMember(new ClaimMember(priyas, inviteId, PriyasSubject, "  "), Now);
        });
    }
}

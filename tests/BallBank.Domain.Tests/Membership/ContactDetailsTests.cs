using BallBank.Domain.Membership;

namespace BallBank.Domain.Tests.Membership;

public class ContactDetailsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private const string JacobsSubject = "test|jacob";
    private const string SamsSubject = "test|sam";

    private static readonly SleeperLeagueSnapshot HollandHogs = new(
        "900000000000000001",
        "Holland Hogs",
        "2026",
        [
            new SleeperLeagueSnapshot.User("100000000000000001", "Jacob", IsCommissioner: true),
            new SleeperLeagueSnapshot.User("100000000000000002", "Sam", IsCommissioner: false),
            new SleeperLeagueSnapshot.User("100000000000000003", "Priya", IsCommissioner: false),
        ],
        [
            new SleeperLeagueSnapshot.Roster(1, "100000000000000001", "Hog Wild"),
            new SleeperLeagueSnapshot.Roster(2, "100000000000000002", "Sam's Slammers"),
            new SleeperLeagueSnapshot.Roster(3, "100000000000000003", TeamName: null),
        ]);

    // Jacob imported the league and keeps its books; Sam holds Sam's Slammers; Priya has not claimed.
    private readonly League _league = HollandHogsWithSamClaimed();

    private Guid Sams => _league.Members.Single(m => m.TeamName == "Sam's Slammers").MemberId;
    private Guid Priyas => _league.Members.Single(m => m.TeamName == "Priya").MemberId;

    private static League HollandHogsWithSamClaimed()
    {
        var events = League.Import(
            new ImportLeague(
                Guid.NewGuid(),
                Guid.NewGuid(),
                HollandHogs,
                HollandHogs.Rosters.ToDictionary(r => r.RosterId, _ => Guid.NewGuid()),
                JacobsSubject,
                "100000000000000001",
                "Jacob",
                SleeperLeagueAlreadyBacks: null),
            Now);
        var league = League.Replay((LeagueImported)events[0], events.Skip(1));
        var sams = league.Members.Single(m => m.SleeperRosterId == 2).MemberId;
        league.Evolve(new MemberClaimed(sams, SamsSubject, "Sam", InviteId: Guid.NewGuid(), Now));
        return league;
    }

    private static RecordContactDetails Record(
        Guid memberId,
        string editor = JacobsSubject,
        string? email = null,
        string? phone = null,
        string? discordUsername = null) =>
        new(memberId, editor, email, phone, discordUsername);

    [Fact]
    public void A_treasurer_records_contact_details_for_a_member_who_has_not_claimed()
    {
        var details = _league.RecordContactDetails(Record(Priyas, email: "priya@example.com", phone: "555 010 0003", discordUsername: "priya"));

        details.ShouldBe(new ContactDetails("priya@example.com", "+15550100003", "priya"));
    }

    [Theory]
    [InlineData("(555) 010-0000")]
    [InlineData("555.010.0000")]
    [InlineData("+1 555 010 0000")]
    [InlineData("555-010-0000")]
    [InlineData("5550100000")]
    [InlineData("1 (555) 010-0000")]
    [InlineData("  +15550100000 ")]
    public void Any_common_way_of_writing_a_US_number_is_the_same_number(string entered)
    {
        _league.RecordContactDetails(Record(Priyas, phone: entered)).Phone.ShouldBe("+15550100000");
    }

    [Theory]
    [InlineData("+44 20 7946 0000")]
    [InlineData("+52 55 1234 5678")]
    [InlineData("011 44 20 7946 0000")]
    public void A_number_outside_the_US_is_refused(string entered)
    {
        Should.Throw<DomainException>(() => _league.RecordContactDetails(Record(Priyas, phone: entered)))
            .Message.ShouldBe("BallBank takes US phone numbers only.");
    }

    [Theory]
    [InlineData("555 0100")]
    [InlineData("555 010 00000")]
    [InlineData("2 555 010 0000")]
    [InlineData("055 010 0000")]
    [InlineData("555-010-0000 ext 12")]
    [InlineData("call me")]
    public void Something_that_is_not_a_US_number_is_refused(string entered)
    {
        Should.Throw<DomainException>(() => _league.RecordContactDetails(Record(Priyas, phone: entered)))
            .Message.ShouldBe("That is not a US phone number. Enter the 10 digits, like (555) 010-0000.");
    }

    [Theory]
    [InlineData("@SomeHandle")]
    [InlineData("somehandle")]
    [InlineData("SomeHandle#1234")]
    [InlineData(" @somehandle#0 ")]
    public void A_Discord_username_is_kept_lowercase_without_the_at_sign_or_discriminator(string entered)
    {
        _league.RecordContactDetails(Record(Priyas, discordUsername: entered)).DiscordUsername.ShouldBe("somehandle");
    }

    [Theory]
    [InlineData("s")]
    [InlineData("some handle")]
    [InlineData("some..handle")]
    [InlineData("this_handle_is_far_too_long_for_discord")]
    public void Something_that_is_not_a_Discord_username_is_refused(string entered)
    {
        Should.Throw<DomainException>(() => _league.RecordContactDetails(Record(Priyas, discordUsername: entered)))
            .Message.ShouldBe("That is not a Discord username: 2 to 32 letters, numbers, underscores and periods.");
    }

    [Fact]
    public void An_email_is_kept_as_entered_without_surrounding_space()
    {
        _league.RecordContactDetails(Record(Priyas, email: "  Priya.P@Example.com ")).Email.ShouldBe("Priya.P@Example.com");
    }

    [Theory]
    [InlineData("priya")]
    [InlineData("priya@")]
    [InlineData("@example.com")]
    [InlineData("pri ya@example.com")]
    [InlineData("Priya <priya@example.com>")]
    [InlineData("priya@example.com, sam@example.com")]
    public void Something_that_is_not_an_email_is_refused(string entered)
    {
        Should.Throw<DomainException>(() => _league.RecordContactDetails(Record(Priyas, email: entered)))
            .Message.ShouldBe("That is not an email address.");
    }

    [Fact]
    public void A_member_records_their_own_contact_details()
    {
        var details = _league.RecordContactDetails(Record(Sams, editor: SamsSubject, email: "sam@example.com", phone: "555 010 0002"));

        details.ShouldBe(new ContactDetails("sam@example.com", "+15550100002", null));
    }

    [Fact]
    public void A_treasurer_records_contact_details_for_a_claimed_member()
    {
        var details = _league.RecordContactDetails(Record(Sams, email: "sam@example.com"));

        details.ShouldBe(new ContactDetails("sam@example.com", null, null));
    }

    [Fact]
    public void A_member_who_is_not_a_treasurer_cannot_change_another_members_contact_details()
    {
        Should.Throw<DomainException>(() => _league.RecordContactDetails(Record(Priyas, editor: SamsSubject, phone: "555 010 0003")))
            .Message.ShouldBe("Only a treasurer of Holland Hogs, or whoever holds Priya, can change its contact details.");
    }

    [Fact]
    public void Someone_holding_no_member_cannot_change_contact_details()
    {
        Should.Throw<DomainException>(() => _league.RecordContactDetails(Record(Priyas, editor: "test|dana", phone: "555 010 0003")));
    }

    [Fact]
    public void A_member_the_league_does_not_have_has_no_contact_details_to_change()
    {
        Should.Throw<DomainException>(() => _league.RecordContactDetails(Record(Guid.NewGuid(), phone: "555 010 0003")))
            .Message.ShouldBe("Holland Hogs has no such member.");
    }

    [Fact]
    public void A_claimed_member_needs_an_email()
    {
        Should.Throw<DomainException>(() => _league.RecordContactDetails(Record(Sams, phone: "555 010 0002")))
            .Message.ShouldBe("Sam's Slammers is claimed, so its contact details need an email.");
    }

    [Fact]
    public void A_member_who_has_not_claimed_needs_nothing()
    {
        _league.RecordContactDetails(Record(Priyas)).ShouldBe(ContactDetails.None);
    }
}

using System.Net;
using System.Net.Http.Json;
using BallBank.Api.Features.Membership;
using BallBank.Domain.Membership;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace BallBank.Integration.Tests.Http;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class ContactDetailsTests(PostgresFixture postgres)
{
    private BallBankApi Api => postgres.Api;
    private IDocumentStore Store => Api.Services.GetRequiredService<IDocumentStore>();

    [Fact]
    public async Task A_treasurer_records_contact_details_for_a_member_who_has_not_claimed()
    {
        var hogs = await HollandHogs.Import(this);

        var response = await Put(hogs.Jacob, hogs, hogs.Priyas, new ContactDetailsRequest("priya@example.com", "(555) 010-0003", "@Priya"));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ContactOf(hogs, hogs.Priyas, asSeenBy: hogs.Jacob))
            .ShouldBe(new ContactDetails("priya@example.com", "+15550100003", "priya"));
    }

    [Fact]
    public async Task A_treasurer_records_contact_details_for_a_claimed_member()
    {
        var hogs = await HollandHogs.Import(this);

        var response = await Put(hogs.Jacob, hogs, hogs.Sams, new ContactDetailsRequest("sam@example.com"));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ContactOf(hogs, hogs.Sams, asSeenBy: hogs.Jacob)).ShouldBe(new ContactDetails("sam@example.com", null, null));
    }

    [Fact]
    public async Task A_member_records_their_own_contact_details()
    {
        var hogs = await HollandHogs.Import(this);

        var response = await Put(hogs.Sam, hogs, hogs.Sams, new ContactDetailsRequest("sam@example.com", "555.010.0002", "SamSlams#0042"));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ContactOf(hogs, hogs.Sams, asSeenBy: hogs.Sam))
            .ShouldBe(new ContactDetails("sam@example.com", "+15550100002", "samslams"));
    }

    [Fact]
    public async Task A_member_is_forbidden_to_change_another_members_contact_details()
    {
        var hogs = await HollandHogs.Import(this);
        await Put(hogs.Jacob, hogs, hogs.Priyas, new ContactDetailsRequest(Phone: "555 010 0003"));

        var response = await Put(hogs.Sam, hogs, hogs.Priyas, new ContactDetailsRequest(Phone: "555 010 0009"));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ContactOf(hogs, hogs.Priyas, asSeenBy: hogs.Jacob)).ShouldBe(new ContactDetails(null, "+15550100003", null));
    }

    [Fact]
    public async Task Someone_outside_the_league_is_forbidden_its_contact_details()
    {
        var hogs = await HollandHogs.Import(this);

        var response = await Put(NewSubject(), hogs, hogs.Priyas, new ContactDetailsRequest(Phone: "555 010 0003"));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Without_a_token_the_caller_is_unauthorized()
    {
        var response = await Api.CreateClient().PutAsJsonAsync(
            $"/leagues/{Guid.NewGuid()}/members/{Guid.NewGuid()}/contact",
            new ContactDetailsRequest(Phone: "555 010 0003"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_member_the_league_does_not_have_is_not_found()
    {
        var hogs = await HollandHogs.Import(this);

        var response = await Put(hogs.Jacob, hogs, Guid.NewGuid(), new ContactDetailsRequest(Phone: "555 010 0003"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("(555) 010-0000")]
    [InlineData("555.010.0000")]
    [InlineData("+1 555 010 0000")]
    public async Task Any_common_way_of_writing_a_US_number_stores_the_same_number(string entered)
    {
        var hogs = await HollandHogs.Import(this);

        (await Put(hogs.Jacob, hogs, hogs.Priyas, new ContactDetailsRequest(Phone: entered))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await StoredContact(hogs, hogs.Priyas)).ShouldNotBeNull().Phone.ShouldBe("+15550100000");
    }

    [Fact]
    public async Task A_number_outside_the_US_is_refused()
    {
        var hogs = await HollandHogs.Import(this);

        var response = await Put(hogs.Jacob, hogs, hogs.Priyas, new ContactDetailsRequest(Phone: "+44 20 7946 0000"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>()).ShouldNotBeNull()
            .Detail.ShouldBe("BallBank takes US phone numbers only.");
        (await StoredContact(hogs, hogs.Priyas)).ShouldBeNull();
    }

    [Theory]
    [InlineData("@SomeHandle")]
    [InlineData("somehandle")]
    public async Task A_Discord_username_is_stored_lowercase_without_the_at_sign(string entered)
    {
        var hogs = await HollandHogs.Import(this);

        (await Put(hogs.Jacob, hogs, hogs.Priyas, new ContactDetailsRequest(DiscordUsername: entered))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await StoredContact(hogs, hogs.Priyas)).ShouldNotBeNull().DiscordUsername.ShouldBe("somehandle");
    }

    [Fact]
    public async Task A_claimed_members_contact_details_need_an_email()
    {
        var hogs = await HollandHogs.Import(this);

        var response = await Put(hogs.Sam, hogs, hogs.Sams, new ContactDetailsRequest(Phone: "555 010 0002"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await StoredContact(hogs, hogs.Sams)).ShouldBeNull();
    }

    [Fact]
    public async Task Clearing_every_field_leaves_nothing_stored_about_the_member()
    {
        var hogs = await HollandHogs.Import(this);
        await Put(hogs.Jacob, hogs, hogs.Priyas, new ContactDetailsRequest("priya@example.com", "555 010 0003", "priya"));

        var response = await Put(hogs.Jacob, hogs, hogs.Priyas, new ContactDetailsRequest("", " ", null));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await StoredContact(hogs, hogs.Priyas)).ShouldBeNull();
        (await ContactOf(hogs, hogs.Priyas, asSeenBy: hogs.Jacob)).ShouldBe(ContactDetails.None);
    }

    [Fact]
    public async Task A_treasurer_sees_every_members_contact_details()
    {
        var hogs = await HollandHogs.Import(this);
        await RecordForEveryone(hogs);

        var list = await MemberList(hogs, asSeenBy: hogs.Jacob);

        list.Members.ToDictionary(m => m.MemberId, m => m.Contact).ShouldBe(new Dictionary<Guid, ContactDetails?>
        {
            [hogs.Jacobs] = new("jacob@example.com", null, null),
            [hogs.Sams] = new("sam@example.com", null, null),
            [hogs.Priyas] = new(null, "+15550100003", null),
            [hogs.Danas] = new("dana@example.com", null, null),
        }, ignoreOrder: true);
    }

    [Fact]
    public async Task A_member_sees_their_own_contact_details_and_no_one_elses()
    {
        var hogs = await HollandHogs.Import(this);
        await RecordForEveryone(hogs);

        var list = await MemberList(hogs, asSeenBy: hogs.Sam);

        list.Members.ToDictionary(m => m.MemberId, m => m.Contact).ShouldBe(new Dictionary<Guid, ContactDetails?>
        {
            [hogs.Jacobs] = null,
            [hogs.Sams] = new("sam@example.com", null, null),
            [hogs.Priyas] = null,
            [hogs.Danas] = null,
        }, ignoreOrder: true);
    }

    [Fact]
    public async Task A_member_with_nothing_recorded_sees_only_their_own_empty_entry()
    {
        var hogs = await HollandHogs.Import(this);
        await Put(hogs.Jacob, hogs, hogs.Sams, new ContactDetailsRequest("sam@example.com"));
        await Put(hogs.Jacob, hogs, hogs.Priyas, new ContactDetailsRequest(Phone: "555 010 0003"));

        var list = await MemberList(hogs, asSeenBy: hogs.Dana);

        list.Members.ToDictionary(m => m.MemberId, m => m.Contact).ShouldBe(new Dictionary<Guid, ContactDetails?>
        {
            [hogs.Jacobs] = null,
            [hogs.Sams] = null,
            [hogs.Priyas] = null,
            [hogs.Danas] = ContactDetails.None,
        }, ignoreOrder: true);
    }

    private async Task RecordForEveryone(HollandHogs hogs)
    {
        (await Put(hogs.Jacob, hogs, hogs.Jacobs, new ContactDetailsRequest("jacob@example.com"))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Put(hogs.Sam, hogs, hogs.Sams, new ContactDetailsRequest("sam@example.com"))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Put(hogs.Jacob, hogs, hogs.Priyas, new ContactDetailsRequest(Phone: "555 010 0003"))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Put(hogs.Dana, hogs, hogs.Danas, new ContactDetailsRequest("dana@example.com"))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private Task<HttpResponseMessage> Put(string subject, HollandHogs hogs, Guid memberId, ContactDetailsRequest request) =>
        Api.CreateClientFor(subject).PutAsJsonAsync($"/leagues/{hogs.LeagueId}/members/{memberId}/contact", request);

    private async Task<LeagueMembers> MemberList(HollandHogs hogs, string asSeenBy) =>
        (await Api.CreateClientFor(asSeenBy).GetFromJsonAsync<LeagueMembers>($"/leagues/{hogs.LeagueId}/members")).ShouldNotBeNull();

    /// <summary>A member's contact details as the member list shows them to <paramref name="asSeenBy"/>.</summary>
    private async Task<ContactDetails?> ContactOf(HollandHogs hogs, Guid memberId, string asSeenBy) =>
        (await MemberList(hogs, asSeenBy)).Members.Single(m => m.MemberId == memberId).Contact;

    /// <summary>What is kept about how to reach a member, in the league's tenant.</summary>
    private async Task<MemberContact?> StoredContact(HollandHogs hogs, Guid memberId)
    {
        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        return await session.LoadAsync<MemberContact>(memberId);
    }

    /// <summary>
    /// Holland Hogs as imported by Jacob, who keeps its books, with Sam holding Sam's Slammers, Priya
    /// holding nothing yet, and Dana holding Team 4.
    /// </summary>
    private sealed record HollandHogs(Guid LeagueId, string Jacob, string Sam, string Dana, Guid Jacobs, Guid Sams, Guid Priyas, Guid Danas)
    {
        public static async Task<HollandHogs> Import(ContactDetailsTests tests)
        {
            var leagueId = Guid.NewGuid();
            var jacob = NewSubject();
            var response = await tests.Api.CreateClientFor(jacob).PostAsJsonAsync(
                $"/leagues/{leagueId}/import",
                new ImportLeagueRequest(tests.Api.Sleeper.CopyOfHollandHogs(), "jacob", "Jacob"));
            response.StatusCode.ShouldBe(HttpStatusCode.Created);

            var sam = NewSubject();
            var dana = NewSubject();
            var league = await tests.ClaimAsync(leagueId, rosterId: 2, sam, "Sam");
            league = await tests.ClaimAsync(leagueId, rosterId: 4, dana, "Dana");

            Guid MemberOf(int rosterId) => league.Members.Single(m => m.SleeperRosterId == rosterId).MemberId;
            return new HollandHogs(leagueId, jacob, sam, dana, MemberOf(1), MemberOf(2), MemberOf(3), MemberOf(4));
        }
    }

    // Written the way a claim writes it, without the invite and the contact details a claim over HTTP needs.
    private async Task<League> ClaimAsync(Guid leagueId, int rosterId, string subject, string displayName)
    {
        await using var session = Store.LightweightSession(leagueId.ToString());
        var league = (await session.Events.AggregateStreamAsync<League>(leagueId)).ShouldNotBeNull();
        var member = league.Members.Single(m => m.SleeperRosterId == rosterId);
        var claimed = new MemberClaimed(member.MemberId, subject, displayName, Guid.NewGuid(), DateTimeOffset.UtcNow);
        session.Events.Append(leagueId, claimed);
        session.Store(new UserMemberships
        {
            Id = subject,
            Leagues = [new LeagueMembership(leagueId, league.Name, league.Season, member.MemberId, [])],
        });
        await session.SaveChangesAsync();

        league.Evolve(claimed);
        return league;
    }

    private static string NewSubject() => $"test|{Guid.NewGuid():N}";
}

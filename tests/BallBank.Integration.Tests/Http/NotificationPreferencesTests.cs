using System.Net;
using System.Net.Http.Json;
using BallBank.Api.Features.Membership;
using BallBank.Api.Features.Notifications;
using BallBank.Domain.Membership;
using BallBank.Domain.Notifications;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace BallBank.Integration.Tests.Http;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class NotificationPreferencesTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Monday = new(2026, 9, 28, 14, 0, 0, TimeSpan.Zero);

    private BallBankApi Api => postgres.Api;
    private IDocumentStore Store => Api.Services.GetRequiredService<IDocumentStore>();

    [Fact]
    public async Task A_member_opts_in_at_the_number_on_record()
    {
        var hogs = await HollandHogs.Import(this);
        await RecordSamsContact(hogs, phone: "555 010 0002");

        using var _ = Api.Clock.Set(Monday);
        var response = await Put(hogs.Sam, hogs, hogs.Sams, new NotificationPreferencesRequest(TextMe: true));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var reading = await Read(hogs.Sam, hogs, hogs.Sams);
        reading.Consent.ShouldBe(new SmsConsent("+15550100002", Monday));
        reading.OptedIn.ShouldBeTrue();
        reading.OptedOut.ShouldBeFalse();
    }

    [Fact]
    public async Task Until_a_member_chooses_quiet_hours_are_nine_at_night_to_nine_in_the_morning_Eastern()
    {
        var hogs = await HollandHogs.Import(this);

        var reading = await Read(hogs.Sam, hogs, hogs.Sams);

        reading.Consent.ShouldBeNull();
        reading.OptedIn.ShouldBeFalse();
        reading.QuietHours.ShouldBe(new QuietHours(21, 9, "America/New_York"));
    }

    [Fact]
    public async Task A_treasurer_cannot_opt_a_member_in()
    {
        var hogs = await HollandHogs.Import(this);
        await RecordSamsContact(hogs, phone: "555 010 0002");

        var response = await Put(hogs.Jacob, hogs, hogs.Sams, new NotificationPreferencesRequest(TextMe: true));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Stored(hogs, hogs.Sams)).ShouldBeNull();
    }

    [Fact]
    public async Task A_member_cannot_opt_another_member_in()
    {
        var hogs = await HollandHogs.Import(this);
        await RecordSamsContact(hogs, phone: "555 010 0002");

        var response = await Put(hogs.Dana, hogs, hogs.Sams, new NotificationPreferencesRequest(TextMe: true));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Stored(hogs, hogs.Sams)).ShouldBeNull();
    }

    [Fact]
    public async Task Someone_outside_the_league_is_forbidden_to_change_or_read_its_preferences()
    {
        var hogs = await HollandHogs.Import(this);
        var stranger = NewSubject();

        (await Put(stranger, hogs, hogs.Sams, new NotificationPreferencesRequest(TextMe: true))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Get(stranger, hogs, hogs.Sams)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Without_a_token_the_caller_is_unauthorized()
    {
        var path = $"/leagues/{Guid.NewGuid()}/members/{Guid.NewGuid()}/notifications";

        (await Api.CreateClient().PutAsJsonAsync(path, new NotificationPreferencesRequest(TextMe: true))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Api.CreateClient().GetAsync(path)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_member_the_league_does_not_have_is_not_found()
    {
        var hogs = await HollandHogs.Import(this);

        (await Put(hogs.Jacob, hogs, Guid.NewGuid(), new NotificationPreferencesRequest(TextMe: true))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Get(hogs.Jacob, hogs, Guid.NewGuid())).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Consent_needs_a_phone_number_on_record()
    {
        var hogs = await HollandHogs.Import(this);
        await RecordSamsContact(hogs, phone: null);

        var response = await Put(hogs.Sam, hogs, hogs.Sams, new NotificationPreferencesRequest(TextMe: true));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>()).ShouldNotBeNull()
            .Detail.ShouldBe("Text messages go to a specific number. Add your phone number first.");
        (await Stored(hogs, hogs.Sams)).ShouldBeNull();
    }

    [Fact]
    public async Task Opting_in_again_keeps_when_consent_was_first_given()
    {
        var hogs = await HollandHogs.Import(this);
        await RecordSamsContact(hogs, phone: "555 010 0002");
        using (Api.Clock.Set(Monday))
        {
            await Put(hogs.Sam, hogs, hogs.Sams, new NotificationPreferencesRequest(TextMe: true));
        }

        using var _ = Api.Clock.Set(Monday.AddDays(3));
        (await Put(hogs.Sam, hogs, hogs.Sams, new NotificationPreferencesRequest(TextMe: true, new QuietHours(22, 8, "America/Chicago"))))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Read(hogs.Sam, hogs, hogs.Sams)).Consent.ShouldBe(new SmsConsent("+15550100002", Monday));
    }

    [Fact]
    public async Task A_member_withdraws_consent_and_keeps_their_quiet_hours()
    {
        var hogs = await HollandHogs.Import(this);
        await RecordSamsContact(hogs, phone: "555 010 0002");
        var chosen = new QuietHours(22, 7, "America/Chicago");
        await Put(hogs.Sam, hogs, hogs.Sams, new NotificationPreferencesRequest(TextMe: true, chosen));

        var response = await Put(hogs.Sam, hogs, hogs.Sams, new NotificationPreferencesRequest(TextMe: false, chosen));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var reading = await Read(hogs.Sam, hogs, hogs.Sams);
        reading.Consent.ShouldBeNull();
        reading.OptedIn.ShouldBeFalse();
        reading.QuietHours.ShouldBe(chosen);
    }

    [Fact]
    public async Task Quiet_hours_are_kept_as_chosen_and_replaced_by_the_next_save()
    {
        var hogs = await HollandHogs.Import(this);

        await Put(hogs.Sam, hogs, hogs.Sams, new NotificationPreferencesRequest(TextMe: false, new QuietHours(22, 7, "America/Chicago")));
        (await Read(hogs.Sam, hogs, hogs.Sams)).QuietHours.ShouldBe(new QuietHours(22, 7, "America/Chicago"));

        await Put(hogs.Sam, hogs, hogs.Sams, new NotificationPreferencesRequest(TextMe: false));
        (await Read(hogs.Sam, hogs, hogs.Sams)).QuietHours.ShouldBe(QuietHours.Default);
    }

    [Theory]
    [InlineData(21, 21, "America/New_York", "Quiet hours must start and end at different hours.")]
    [InlineData(25, 9, "America/New_York", "Quiet hours start and end on an hour, from 0 (midnight) to 23.")]
    [InlineData(21, 9, "Mars/Olympus_Mons", "That is not a time zone. Use one like America/New_York.")]
    public async Task Quiet_hours_that_are_not_hours_in_a_time_zone_are_refused(int start, int end, string zone, string reason)
    {
        var hogs = await HollandHogs.Import(this);

        var response = await Put(hogs.Sam, hogs, hogs.Sams, new NotificationPreferencesRequest(TextMe: false, new QuietHours(start, end, zone)));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>()).ShouldNotBeNull().Detail.ShouldBe(reason);
        (await Stored(hogs, hogs.Sams)).ShouldBeNull();
    }

    [Fact]
    public async Task A_treasurer_reads_what_a_member_has_said()
    {
        var hogs = await HollandHogs.Import(this);
        await RecordSamsContact(hogs, phone: "555 010 0002");
        await Put(hogs.Sam, hogs, hogs.Sams, new NotificationPreferencesRequest(TextMe: true));

        var reading = await Read(hogs.Jacob, hogs, hogs.Sams);

        reading.OptedIn.ShouldBeTrue();
        reading.Consent.ShouldNotBeNull().Phone.ShouldBe("+15550100002");
    }

    [Fact]
    public async Task A_member_is_forbidden_to_read_another_members_preferences()
    {
        var hogs = await HollandHogs.Import(this);

        (await Get(hogs.Dana, hogs, hogs.Sams)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Saving_a_different_phone_number_clears_consent()
    {
        var hogs = await HollandHogs.Import(this);
        await RecordSamsContact(hogs, phone: "555 010 0002");
        var chosen = new QuietHours(22, 7, "America/Chicago");
        await Put(hogs.Sam, hogs, hogs.Sams, new NotificationPreferencesRequest(TextMe: true, chosen));

        await RecordSamsContact(hogs, phone: "555 010 0009");

        var reading = await Read(hogs.Sam, hogs, hogs.Sams);
        reading.Consent.ShouldBeNull();
        reading.OptedIn.ShouldBeFalse();
        reading.QuietHours.ShouldBe(chosen);
    }

    [Fact]
    public async Task A_treasurer_changing_the_phone_number_clears_consent_too()
    {
        var hogs = await HollandHogs.Import(this);
        await RecordSamsContact(hogs, phone: "555 010 0002");
        await Put(hogs.Sam, hogs, hogs.Sams, new NotificationPreferencesRequest(TextMe: true));

        var response = await Api.CreateClientFor(hogs.Jacob).PutAsJsonAsync(
            $"/leagues/{hogs.LeagueId}/members/{hogs.Sams}/contact", new ContactDetailsRequest("sam@example.com", "555 010 0009"));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Read(hogs.Sam, hogs, hogs.Sams)).Consent.ShouldBeNull();
    }

    [Fact]
    public async Task Removing_the_phone_number_clears_consent()
    {
        var hogs = await HollandHogs.Import(this);
        await RecordSamsContact(hogs, phone: "555 010 0002");
        await Put(hogs.Sam, hogs, hogs.Sams, new NotificationPreferencesRequest(TextMe: true));

        await RecordSamsContact(hogs, phone: null);

        (await Read(hogs.Sam, hogs, hogs.Sams)).Consent.ShouldBeNull();
    }

    [Fact]
    public async Task Saving_the_same_phone_number_again_keeps_consent()
    {
        var hogs = await HollandHogs.Import(this);
        await RecordSamsContact(hogs, phone: "555 010 0002");
        await Put(hogs.Sam, hogs, hogs.Sams, new NotificationPreferencesRequest(TextMe: true));

        await RecordSamsContact(hogs, phone: "(555) 010-0002");

        (await Read(hogs.Sam, hogs, hogs.Sams)).OptedIn.ShouldBeTrue();
    }

    [Fact]
    public async Task Consent_given_at_the_old_number_is_not_consent_at_the_new_one()
    {
        var hogs = await HollandHogs.Import(this);
        await RecordSamsContact(hogs, phone: "555 010 0002");
        using (Api.Clock.Set(Monday))
        {
            await Put(hogs.Sam, hogs, hogs.Sams, new NotificationPreferencesRequest(TextMe: true));
        }

        await RecordSamsContact(hogs, phone: "555 010 0009");
        using var _ = Api.Clock.Set(Monday.AddDays(3));
        await Put(hogs.Sam, hogs, hogs.Sams, new NotificationPreferencesRequest(TextMe: true));

        (await Read(hogs.Sam, hogs, hogs.Sams)).Consent.ShouldBe(new SmsConsent("+15550100009", Monday.AddDays(3)));
    }

    [Fact]
    public async Task What_a_holder_chose_goes_with_their_claim_when_it_is_revoked()
    {
        var hogs = await HollandHogs.Import(this);
        await RecordSamsContact(hogs, phone: "555 010 0002");
        await Put(hogs.Sam, hogs, hogs.Sams, new NotificationPreferencesRequest(TextMe: true, new QuietHours(22, 7, "America/Chicago")));

        var revoke = new HttpRequestMessage(HttpMethod.Delete, $"/leagues/{hogs.LeagueId}/members/{hogs.Sams}/claims/{Uri.EscapeDataString(hogs.Sam)}")
        {
            Content = JsonContent.Create(new RevokeClaimRequest("Wrong person claimed it")),
        };
        (await Api.CreateClientFor(hogs.Jacob).SendAsync(revoke)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var reading = await Read(hogs.Jacob, hogs, hogs.Sams);
        reading.Consent.ShouldBeNull();
        reading.OptedIn.ShouldBeFalse();
        reading.QuietHours.ShouldBe(QuietHours.Default);
    }

    [Fact]
    public async Task A_number_that_has_opted_out_is_not_opted_in_whatever_consent_says()
    {
        const string Number = "+15550100871";
        var hogs = await HollandHogs.Import(this);
        await RecordSamsContact(hogs, phone: "555 010 0871");
        await Put(hogs.Sam, hogs, hogs.Sams, new NotificationPreferencesRequest(TextMe: true));

        await using (var session = Store.LightweightSession())
        {
            session.Store(new PhoneOptOut { Id = Number, OptedOutAt = Monday });
            await session.SaveChangesAsync();
        }

        try
        {
            var reading = await Read(hogs.Sam, hogs, hogs.Sams);
            reading.OptedOut.ShouldBeTrue();
            reading.OptedIn.ShouldBeFalse();
            reading.Consent.ShouldNotBeNull().Phone.ShouldBe(Number);

            var list = await MemberList(hogs, asSeenBy: hogs.Jacob);
            list.Members.Single(m => m.MemberId == hogs.Sams).TextsOptedIn.ShouldBe(false);
        }
        finally
        {
            await using var session = Store.LightweightSession();
            session.Delete<PhoneOptOut>(Number);
            await session.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task A_treasurer_sees_on_every_row_whether_the_member_is_opted_in()
    {
        var hogs = await HollandHogs.Import(this);
        await RecordSamsContact(hogs, phone: "555 010 0002");
        await Put(hogs.Sam, hogs, hogs.Sams, new NotificationPreferencesRequest(TextMe: true));

        var list = await MemberList(hogs, asSeenBy: hogs.Jacob);

        list.Members.ToDictionary(m => m.MemberId, m => m.TextsOptedIn).ShouldBe(new Dictionary<Guid, bool?>
        {
            [hogs.Jacobs] = false,
            [hogs.Sams] = true,
            [hogs.Priyas] = false,
            [hogs.Danas] = false,
        }, ignoreOrder: true);
    }

    [Fact]
    public async Task A_member_sees_whether_they_are_opted_in_and_nobody_elses()
    {
        var hogs = await HollandHogs.Import(this);
        await RecordSamsContact(hogs, phone: "555 010 0002");
        await Put(hogs.Sam, hogs, hogs.Sams, new NotificationPreferencesRequest(TextMe: true));

        (await MemberList(hogs, asSeenBy: hogs.Sam)).Members.ToDictionary(m => m.MemberId, m => m.TextsOptedIn).ShouldBe(new Dictionary<Guid, bool?>
        {
            [hogs.Jacobs] = null,
            [hogs.Sams] = true,
            [hogs.Priyas] = null,
            [hogs.Danas] = null,
        }, ignoreOrder: true);
        (await MemberList(hogs, asSeenBy: hogs.Dana)).Members.Single(m => m.MemberId == hogs.Danas).TextsOptedIn.ShouldBe(false);
    }

    private async Task RecordSamsContact(HollandHogs hogs, string? phone)
    {
        var response = await Api.CreateClientFor(hogs.Sam).PutAsJsonAsync(
            $"/leagues/{hogs.LeagueId}/members/{hogs.Sams}/contact", new ContactDetailsRequest("sam@example.com", phone));
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private Task<HttpResponseMessage> Put(string subject, HollandHogs hogs, Guid memberId, NotificationPreferencesRequest request) =>
        Api.CreateClientFor(subject).PutAsJsonAsync($"/leagues/{hogs.LeagueId}/members/{memberId}/notifications", request);

    private Task<HttpResponseMessage> Get(string subject, HollandHogs hogs, Guid memberId) =>
        Api.CreateClientFor(subject).GetAsync($"/leagues/{hogs.LeagueId}/members/{memberId}/notifications");

    private async Task<NotificationPreferencesReading> Read(string subject, HollandHogs hogs, Guid memberId)
    {
        var response = await Get(subject, hogs, memberId);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<NotificationPreferencesReading>()).ShouldNotBeNull();
    }

    private async Task<LeagueMembers> MemberList(HollandHogs hogs, string asSeenBy) =>
        (await Api.CreateClientFor(asSeenBy).GetFromJsonAsync<LeagueMembers>($"/leagues/{hogs.LeagueId}/members")).ShouldNotBeNull();

    /// <summary>What is kept about a member's preferences, in the league's tenant.</summary>
    private async Task<NotificationPreferences?> Stored(HollandHogs hogs, Guid memberId)
    {
        await using var session = Store.QuerySession(hogs.LeagueId.ToString());
        return await session.LoadAsync<NotificationPreferences>(memberId);
    }

    /// <summary>
    /// Holland Hogs as imported by Jacob, who keeps its books, with Sam holding Sam's Slammers, Priya
    /// holding nothing yet, and Dana holding Team 4.
    /// </summary>
    private sealed record HollandHogs(Guid LeagueId, string Jacob, string Sam, string Dana, Guid Jacobs, Guid Sams, Guid Priyas, Guid Danas)
    {
        public static async Task<HollandHogs> Import(NotificationPreferencesTests tests)
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

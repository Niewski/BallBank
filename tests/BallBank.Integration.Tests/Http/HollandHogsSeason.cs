using System.Net;
using System.Net.Http.Json;
using BallBank.Api;
using BallBank.Api.Features.Membership;
using BallBank.Api.Features.Treasury;
using BallBank.Domain.Membership;
using BallBank.Domain.Treasury;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace BallBank.Integration.Tests.Http;

/// <summary>
/// Holland Hogs as imported by Jacob, who keeps its books, with Sam holding Sam's Slammers and the
/// 2026 season open at $50 due <see cref="DueDate"/>.
/// </summary>
public sealed record HollandHogsSeason(Guid LeagueId, string Jacob, Guid Jacobs, string Sam, Guid Sams)
{
    public static readonly DateOnly DueDate = new(2026, 10, 1);

    public Guid AccountOf(Guid memberId) => SeasonIds.AccountId(SeasonIds.SeasonId(LeagueId, "2026"), memberId);

    public static async Task<HollandHogsSeason> Open(BallBankApi api)
    {
        var leagueId = Guid.NewGuid();
        var jacob = NewSubject();
        var jacobsClient = api.CreateClientFor(jacob);
        (await jacobsClient.PostAsJsonAsync(
            $"/leagues/{leagueId}/import",
            new ImportLeagueRequest(api.Sleeper.CopyOfHollandHogs(), "jacob", "Jacob"))).StatusCode.ShouldBe(HttpStatusCode.Created);

        var sam = NewSubject();
        var league = await ClaimAsync(api, leagueId, rosterId: 2, sam, "Sam");

        var open = new HttpRequestMessage(HttpMethod.Post, $"/leagues/{leagueId}/seasons")
        {
            Content = JsonContent.Create(new OpenSeasonRequest("2026", 50m, DueDate)),
        };
        open.Headers.Add(Idempotency.Header, Guid.NewGuid().ToString());
        (await jacobsClient.SendAsync(open)).StatusCode.ShouldBe(HttpStatusCode.Created);

        return new HollandHogsSeason(
            leagueId,
            jacob,
            league.Members.Single(m => m.SleeperRosterId == 1).MemberId,
            sam,
            league.Members.Single(m => m.SleeperRosterId == 2).MemberId);
    }

    // Written the way a claim writes it, without the invite and the contact details a claim over HTTP needs.
    private static async Task<League> ClaimAsync(BallBankApi api, Guid leagueId, int rosterId, string subject, string displayName)
    {
        await using var session = api.Services.GetRequiredService<IDocumentStore>().LightweightSession(leagueId.ToString());
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

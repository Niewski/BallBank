using BallBank.Domain.Treasury;
using Marten;

namespace BallBank.Integration.Tests;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class EventStoreTenancyTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_account_aggregates_from_its_events()
    {
        var league = $"league-{Guid.NewGuid():N}";
        var accountId = Guid.NewGuid();
        var treasurer = Guid.NewGuid();
        var attestationId = Guid.NewGuid();

        await using (var session = postgres.Store.LightweightSession(league))
        {
            session.Events.StartStream<MemberAccount>(
                accountId,
                new AccountOpened(accountId, Guid.NewGuid(), "2026", Guid.NewGuid(), Now),
                new DuesAssessed(Guid.NewGuid(), 50m, new DateOnly(2026, 10, 1), "Season dues", treasurer, Now),
                new PaymentAttested(attestationId, 50m, PaymentRail.Venmo, "VN-1234", Guid.NewGuid(), Now),
                new PaymentConfirmed(attestationId, treasurer, Now));

            await session.SaveChangesAsync();
        }

        await using var query = postgres.Store.QuerySession(league);
        var account = await query.Events.AggregateStreamAsync<MemberAccount>(accountId);

        account.ShouldNotBeNull();
        account!.Id.ShouldBe(accountId);
        account.Assessed.ShouldBe(50m);
        account.Confirmed.ShouldBe(50m);
        account.Balance.ShouldBe(0m);
        account.PendingAttestations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Events_of_one_league_are_invisible_to_another()
    {
        var leagueA = $"league-{Guid.NewGuid():N}";
        var leagueB = $"league-{Guid.NewGuid():N}";
        var accountId = Guid.NewGuid();

        await using (var session = postgres.Store.LightweightSession(leagueA))
        {
            session.Events.StartStream<MemberAccount>(
                accountId,
                new AccountOpened(accountId, Guid.NewGuid(), "2026", Guid.NewGuid(), Now));

            await session.SaveChangesAsync();
        }

        await using (var sameLeague = postgres.Store.QuerySession(leagueA))
        {
            var visible = await sameLeague.Events.AggregateStreamAsync<MemberAccount>(accountId);
            visible.ShouldNotBeNull();
        }

        await using (var otherLeague = postgres.Store.QuerySession(leagueB))
        {
            var hidden = await otherLeague.Events.AggregateStreamAsync<MemberAccount>(accountId);
            hidden.ShouldBeNull();
        }
    }
}

using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using BallBank.Domain.Treasury;
using JasperFx.Events;
using JasperFx.Events.Grouping;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;

namespace BallBank.Api.Features.Treasury;

/// <summary>
/// The treasurer's dashboard for one season (ADR-0006): what the season's accounts add up to and who
/// owes what. One document per season per league, keyed by season id and built by the projection
/// daemon from every account's stream, so it trails the events by a moment; no command writes it, and
/// it can be thrown away and rebuilt from them. Each account row keeps the sums its events add up to;
/// the figures and balances are computed from those (rule 3).
/// </summary>
public sealed class LeaguePot
{
    /// <summary>The season id.</summary>
    public Guid Id { get; set; }

    public Guid LeagueId { get; set; }
    public string Season { get; set; } = string.Empty;
    public List<PotAccount> Accounts { get; set; } = [];

    /// <summary>The global sequence of the last event applied to this document.</summary>
    public long LastSequence { get; set; }

    /// <summary>When the last event applied to this document was recorded: how current the figures are.</summary>
    public DateTimeOffset AsOf { get; set; }

    [JsonIgnore] public decimal Assessed => Accounts.Sum(a => a.Assessed);
    [JsonIgnore] public decimal Confirmed => Accounts.Sum(a => a.Confirmed);
    [JsonIgnore] public decimal Refunded => Accounts.Sum(a => a.Refunded);

    /// <summary>The adjustments, signed: refunds included, as they raise what a member owes.</summary>
    [JsonIgnore] public decimal Adjusted => Accounts.Sum(a => a.Adjusted);

    /// <summary>What the members paid in, less what was refunded.</summary>
    [JsonIgnore] public decimal Pot => Confirmed - Refunded;

    /// <summary>What members still owe the pot: the balances above zero, added up.</summary>
    [JsonIgnore] public decimal Outstanding => Accounts.Where(a => a.Balance > 0).Sum(a => a.Balance);

    /// <summary>What the pot owes members: the balances below zero, added up, as a positive amount.</summary>
    [JsonIgnore] public decimal Owed => -Accounts.Where(a => a.Balance < 0).Sum(a => a.Balance);

    [JsonIgnore] public int PendingAttestations => Accounts.Sum(a => a.PendingAttestations);
}

/// <summary>One account's row of a <see cref="LeaguePot"/>.</summary>
public sealed class PotAccount
{
    public Guid AccountId { get; set; }
    public Guid MemberId { get; set; }
    public decimal Assessed { get; set; }
    public decimal Confirmed { get; set; }
    public decimal Adjusted { get; set; }
    public decimal Refunded { get; set; }

    /// <summary>The earliest date anything assessed to this account was due; <c>null</c> until something is.</summary>
    public DateOnly? EarliestDueDate { get; set; }

    /// <summary>The attestations waiting for a treasurer, by id, with their amounts: a confirmation names only the id.</summary>
    public Dictionary<Guid, decimal> Pending { get; set; } = [];

    /// <summary>Positive: the member owes the pot. Negative: the pot owes the member.</summary>
    [JsonIgnore] public decimal Balance => Assessed - Confirmed + Adjusted;

    [JsonIgnore] public int PendingAttestations => Pending.Count;
}

/// <summary>
/// Builds <see cref="LeaguePot"/> from the streams of every <see cref="MemberAccount"/> of a season,
/// run by the projection daemon. Wired explicitly for the same reason as <see cref="MemberAccountProjection"/>.
/// </summary>
public sealed class LeaguePotProjection : MultiStreamProjection<LeaguePot, Guid>
{
    public const string ProjectionName = "LeaguePot";

    public LeaguePotProjection(ILogger<LeaguePotProjection> logger)
    {
        Name = ProjectionName;

        IncludeType<AccountOpened>();
        IncludeType<DuesAssessed>();
        IncludeType<PaymentAttested>();
        IncludeType<PaymentConfirmed>();
        IncludeType<PaymentRejected>();
        IncludeType<AdjustmentPosted>();

        CustomGrouping(new SeasonGrouper(logger));
    }

    public override LeaguePot? Evolve(LeaguePot? snapshot, Guid id, IEvent e)
    {
        if (e.Data is AccountOpened opened)
        {
            snapshot ??= new LeaguePot { Id = id, LeagueId = opened.LeagueId, Season = opened.Season };
            if (snapshot.Accounts.All(a => a.AccountId != opened.AccountId))
            {
                snapshot.Accounts.Add(new PotAccount { AccountId = opened.AccountId, MemberId = opened.MemberId });
            }
        }
        else if (snapshot?.Accounts.Find(a => a.AccountId == e.StreamId) is { } account)
        {
            Fold(account, e.Data);
        }
        else
        {
            return snapshot;
        }

        snapshot.LastSequence = e.Sequence;
        snapshot.AsOf = e.Timestamp;

        // Both ends are the wall clock: the event was stamped by the store, whatever the app's clock says.
        ProjectionLagMetric.Record(ProjectionName, e.TenantId, e.Data.GetType().Name, TimeProvider.System.GetUtcNow() - e.Timestamp);
        return snapshot;
    }

    // Not named Apply: Marten claims that name for its own conventions and refuses an Evolve override beside it.
    private static void Fold(PotAccount account, object @event)
    {
        switch (@event)
        {
            case DuesAssessed assessed:
                account.Assessed += assessed.Amount;
                account.EarliestDueDate = account.EarliestDueDate is { } due && due <= assessed.DueDate ? due : assessed.DueDate;
                break;
            case PaymentAttested attested:
                account.Pending[attested.AttestationId] = attested.Amount;
                break;
            case PaymentConfirmed confirmed when account.Pending.Remove(confirmed.AttestationId, out var amount):
                account.Confirmed += amount;
                break;
            case PaymentRejected rejected:
                account.Pending.Remove(rejected.AttestationId);
                break;
            case AdjustmentPosted adjusted:
                account.Adjusted += adjusted.Amount;
                if (adjusted.Refund)
                {
                    account.Refunded += adjusted.Amount;
                }

                break;
        }
    }

    /// <summary>
    /// Sends each account event to its season's document. The stream is the account, whose opening
    /// event names its league and season; that is read once per account, and learnt from the batch
    /// itself when the account is opened in it. Says which leagues a batch is for, under their
    /// <c>tenant.id</c>, as the daemon works through all of them.
    /// </summary>
    private sealed class SeasonGrouper(ILogger logger) : IAggregateGrouper<Guid>
    {
        private readonly ConcurrentDictionary<(string Tenant, Guid Account), Guid> _seasons = new();

        public async Task Group(IQuerySession session, IReadOnlyList<IEvent> accountEvents, IEventGrouping<Guid> grouping)
        {
            foreach (var league in accountEvents.GroupBy(e => e.TenantId))
            {
                using var scope = logger.BeginTenantScope(league.Key);
                logger.LogInformation("{Projection} is applying {Count} events of league {LeagueId}", ProjectionName, league.Count(), league.Key);
            }

            foreach (var e in accountEvents)
            {
                if (e.Data is AccountOpened opened)
                {
                    _seasons[(e.TenantId, opened.AccountId)] = SeasonIds.SeasonId(opened.LeagueId, opened.Season);
                }
            }

            foreach (var tenant in accountEvents.Where(e => !_seasons.ContainsKey((e.TenantId, e.StreamId))).GroupBy(e => e.TenantId))
            {
                var accounts = tenant.Select(e => e.StreamId).Distinct().ToArray();
                var opened = await session.ForTenant(tenant.Key).Events
                    .QueryRawEventDataOnly<AccountOpened>()
                    .Where(o => accounts.Contains(o.AccountId))
                    .ToListAsync();

                foreach (var o in opened)
                {
                    _seasons[(tenant.Key, o.AccountId)] = SeasonIds.SeasonId(o.LeagueId, o.Season);
                }
            }

            foreach (var e in accountEvents)
            {
                if (_seasons.TryGetValue((e.TenantId, e.StreamId), out var seasonId))
                {
                    grouping.AddEvent(seasonId, e);
                }
            }
        }
    }
}

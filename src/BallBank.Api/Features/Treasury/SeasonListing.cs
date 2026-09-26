using BallBank.Domain.Treasury;
using JasperFx.Events;
using Marten.Events.Aggregation;

namespace BallBank.Api.Features.Treasury;

/// <summary>
/// The one fact <c>GET /leagues/{leagueId}/seasons</c> needs about a season, stored rather than
/// replayed: unlike <see cref="Season"/> — the domain aggregate <see cref="OpenSeasonEndpoint"/>
/// decides against, kept private-set and rebuilt live — this is a plain read model, so Marten's
/// document serializer can round-trip it.
/// </summary>
public sealed class SeasonListing
{
    /// <summary>The season id.</summary>
    public Guid Id { get; set; }

    public Guid LeagueId { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal DuesAmount { get; set; }
    public DateOnly DueDate { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
}

/// <summary>Builds <see cref="SeasonListing"/> from a <see cref="Season"/> stream.</summary>
public sealed class SeasonListingProjection : SingleStreamProjection<SeasonListing, Guid>
{
    public override SeasonListing? Evolve(SeasonListing? snapshot, Guid id, IEvent e) =>
        e.Data is SeasonOpened opened
            ? new SeasonListing
            {
                Id = opened.SeasonId,
                LeagueId = opened.LeagueId,
                Label = opened.Label,
                DuesAmount = opened.DuesAmount,
                DueDate = opened.DueDate,
                OpenedAt = opened.OpenedAt,
            }
            : snapshot;
}

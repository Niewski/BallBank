using BallBank.Domain.Membership;
using JasperFx.Events;
using Marten.Events.Aggregation;

namespace BallBank.Api.Features.Membership;

/// <summary>
/// Rebuilds a <see cref="League"/> from its stream, wired explicitly for the same reason as
/// <see cref="Treasury.MemberAccountProjection"/>: the package-free domain cannot host Marten's conventions.
/// </summary>
public sealed class LeagueProjection : SingleStreamProjection<League, Guid>
{
    public override League? Evolve(League? snapshot, Guid id, IEvent e)
    {
        if (snapshot is null)
        {
            return e.Data is LeagueImported imported ? League.Replay(imported) : null;
        }

        snapshot.Evolve(e.Data);
        return snapshot;
    }
}

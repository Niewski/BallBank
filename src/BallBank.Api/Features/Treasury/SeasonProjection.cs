using BallBank.Domain.Treasury;
using JasperFx.Events;
using Marten.Events.Aggregation;

namespace BallBank.Api.Features.Treasury;

/// <summary>
/// Rebuilds a <see cref="Season"/> from its stream, wired explicitly for the same reason as
/// <see cref="MemberAccountProjection"/>: the package-free domain cannot host Marten's conventions.
/// </summary>
public sealed class SeasonProjection : SingleStreamProjection<Season, Guid>
{
    public override Season? Evolve(Season? snapshot, Guid id, IEvent e)
    {
        if (snapshot is null)
        {
            return e.Data is SeasonOpened opened ? Season.Replay(opened) : null;
        }

        snapshot.Evolve(e.Data);
        return snapshot;
    }
}

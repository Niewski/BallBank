using BallBank.Domain.Treasury;
using JasperFx.Events;
using Marten.Events.Aggregation;

namespace BallBank.Api.Features.Treasury;

/// <summary>
/// Rebuilds a <see cref="MemberAccount"/> from its stream. Marten only dispatches conventional
/// Create/Apply methods through a source generator that must run in the aggregate's own assembly,
/// and <c>BallBank.Domain</c> takes no packages, so evolution is wired here explicitly instead.
/// </summary>
public sealed class MemberAccountProjection : SingleStreamProjection<MemberAccount, Guid>
{
    public override MemberAccount? Evolve(MemberAccount? snapshot, Guid id, IEvent e)
    {
        if (snapshot is null)
        {
            return e.Data is AccountOpened opened ? MemberAccount.Replay(opened) : null;
        }

        snapshot.Evolve(e.Data);
        return snapshot;
    }
}

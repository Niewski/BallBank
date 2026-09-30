using Marten;
using Wolverine;
using Wolverine.Marten;
using Wolverine.Marten.Publishing;

namespace BallBank.Api.Features.Notifications;

public static class ForwardedSessions
{
    /// <summary>
    /// A session on the league's tenant that hands the events it commits to Wolverine, in the transaction that
    /// commits them (ADR-0008). Wolverine forwards the events only of a session its own factory opened.
    /// </summary>
    public static IDocumentSession ForwardingSession(this OutboxedSessionFactory factory, IMessageContext context, Guid leagueId)
    {
        context.TenantId = leagueId.ToString();
        return factory.OpenSession(context);
    }
}

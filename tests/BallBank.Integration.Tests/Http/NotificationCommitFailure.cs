using System.Collections.Concurrent;
using BallBank.Api.Features.Notifications;
using Marten;

namespace BallBank.Integration.Tests.Http;

/// <summary>
/// Makes the commit of a notification fail on purpose: while a league is held, a session that is about to
/// commit a <see cref="Notification"/> throws instead, with everything else that session queued still pending.
/// </summary>
public sealed class NotificationCommitFailure : DocumentSessionListenerBase
{
    private readonly ConcurrentDictionary<string, bool> _failing = new();

    public IDisposable Hold(Guid leagueId)
    {
        var tenantId = leagueId.ToString();
        _failing[tenantId] = true;
        return new Released(() => _failing.TryRemove(tenantId, out _));
    }

    public override Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token) =>
        _failing.ContainsKey(session.TenantId) && session.PendingChanges.InsertsFor<Notification>().Any()
            ? throw new InvalidOperationException("The commit failed, as it was made to.")
            : Task.CompletedTask;

    private sealed class Released(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}

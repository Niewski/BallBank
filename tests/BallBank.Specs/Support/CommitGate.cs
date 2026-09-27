using System.Collections.Concurrent;
using Marten;

namespace BallBank.Specs.Support;

/// <summary>
/// Makes "at once" happen on purpose: while a league is held, each of its sessions waits at the point
/// of committing until the expected number have got there, so every racing request has read what it
/// decides on before any of them commits.
/// </summary>
public sealed class CommitGate : DocumentSessionListenerBase
{
    private readonly ConcurrentDictionary<string, Rendezvous> _held = new();

    /// <summary>Holds the league's next <paramref name="commits"/> commits back until they have all arrived.</summary>
    public IDisposable Hold(Guid leagueId, int commits)
    {
        var tenantId = leagueId.ToString();
        _held[tenantId] = new Rendezvous(commits);
        return new Released(() => _held.TryRemove(tenantId, out _));
    }

    public override Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token) =>
        _held.TryGetValue(session.TenantId, out var rendezvous) ? rendezvous.ArriveAsync(token) : Task.CompletedTask;

    private sealed class Rendezvous(int expected)
    {
        private readonly TaskCompletionSource _all = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public Task ArriveAsync(CancellationToken token)
        {
            if (Interlocked.Increment(ref _arrived) >= expected)
            {
                _all.TrySetResult();
            }

            return _all.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
        }
    }

    private sealed class Released(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}

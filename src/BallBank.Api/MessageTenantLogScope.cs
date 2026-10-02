using Wolverine;

namespace BallBank.Api;

/// <summary>
/// Wolverine middleware: a message is handled under the league it belongs to, so what its handler logs
/// (and what Marten logs for it) carries <c>tenant.id</c>, as it does on the request that caused it.
/// Wolverine tags the handler's span with it already. What the runtime itself logs around a handler
/// (a message arriving) is written outside this scope and carries the message's trace id only.
/// </summary>
public sealed class MessageTenantLogScope
{
    private IDisposable? _scope;

    // Synchronous, so the scope is still open for the rest of the generated handler.
    public void Before(Envelope envelope, ILogger logger)
    {
        if (envelope.TenantId is { } tenant)
        {
            _scope = logger.BeginTenantScope(tenant);
        }
    }

    public void Finally() => _scope?.Dispose();
}

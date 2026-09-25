using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace BallBank.Integration.Tests.Http;

/// <summary>
/// A request sent under a trace of its own, so what the API recorded about it (its server span and the
/// logs written while handling it) can be told apart from every other request the test host serves.
/// </summary>
public sealed class TracedRequest : IDisposable
{
    private readonly TaskCompletionSource<Activity> _serverSpan = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ActivityListener _listener;

    public TracedRequest()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
                options.TraceId == TraceId ? ActivitySamplingResult.AllDataAndRecorded : ActivitySamplingResult.None,
            ActivityStopped = activity =>
            {
                if (activity.TraceId == TraceId)
                {
                    _serverSpan.TrySetResult(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public ActivityTraceId TraceId { get; } = ActivityTraceId.CreateRandom();

    /// <summary>
    /// The span the API opened for the request, once it has ended. The host ends it after the client
    /// has the response, and everything the API logs about the request is written by then.
    /// </summary>
    public Task<Activity> ServerSpan() => _serverSpan.Task.WaitAsync(TimeSpan.FromSeconds(10));

    public HttpRequestMessage Get(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("traceparent", $"00-{TraceId}-{ActivitySpanId.CreateRandom()}-01");
        return request;
    }

    public void Dispose() => _listener.Dispose();
}

/// <summary>One log entry the API wrote, with the trace it was written under and the scopes around it.</summary>
public sealed record CapturedLog(string Category, string Message, string? TraceId, IReadOnlyDictionary<string, object?> Scope);

/// <summary>Keeps every log entry the API writes, at every level, with its scopes.</summary>
public sealed class CapturedLogs : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<CapturedLog> _entries = new();
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public IEnumerable<CapturedLog> UnderTrace(ActivityTraceId traceId) =>
        _entries.Where(e => e.TraceId == traceId.ToString());

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
    }

    private sealed class Logger(CapturedLogs logs, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => logs._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var scope = new Dictionary<string, object?>();
            logs._scopes.ForEachScope(
                (value, into) =>
                {
                    if (value is IEnumerable<KeyValuePair<string, object?>> pairs)
                    {
                        foreach (var (key, item) in pairs)
                        {
                            into[key] = item;
                        }
                    }
                },
                scope);

            logs._entries.Enqueue(new CapturedLog(category, formatter(state, exception), Activity.Current?.TraceId.ToString(), scope));
        }
    }
}

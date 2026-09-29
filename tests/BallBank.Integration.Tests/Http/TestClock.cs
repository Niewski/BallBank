namespace BallBank.Integration.Tests.Http;

/// <summary>
/// The API's clock in tests: the wall clock until a test sets it, then that time until the test
/// advances it or puts it back. Only "now" moves; timers and timestamps stay real, so timeouts and
/// retries behave as they would without it.
/// </summary>
public sealed class TestClock : TimeProvider
{
    private readonly Lock _lock = new();
    private DateTimeOffset? _now;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_lock)
        {
            return _now ?? System.GetUtcNow();
        }
    }

    /// <summary>Stops the clock at <paramref name="now"/>. Dispose the result to put back the clock as it was.</summary>
    public IDisposable Set(DateTimeOffset now) => Move(_ => now.ToUniversalTime());

    /// <summary>Stops the clock <paramref name="by"/> later than it reads now. Dispose the result to put back the clock as it was.</summary>
    public IDisposable Advance(TimeSpan by) => Move(now => now + by);

    private Restore Move(Func<DateTimeOffset, DateTimeOffset> to)
    {
        lock (_lock)
        {
            var was = _now;
            _now = to(_now ?? System.GetUtcNow());
            return new Restore(this, was);
        }
    }

    private sealed class Restore(TestClock clock, DateTimeOffset? was) : IDisposable
    {
        public void Dispose()
        {
            lock (clock._lock)
            {
                clock._now = was;
            }
        }
    }
}

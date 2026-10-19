using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Goldmark.Api.Health;

/// <summary>
/// Bounds the cost of the anonymous readiness probe: concurrent requests share one in-flight
/// check, and a result is reused for <see cref="MaxAge"/>. However many requests arrive, SQL
/// Server sees at most one probe connection per <see cref="MaxAge"/>.
/// <para>
/// The shared probe runs on its own <see cref="ProbeTimeout"/>, never on the token of the
/// request that started it, so a client that disconnects cannot make a cancellation the
/// cached result for everyone. Cache age is measured with the monotonic timestamp, so a
/// wall-clock change cannot keep a stale result alive.
/// </para>
/// </summary>
internal sealed class ReadinessGate(TimeProvider time) : IDisposable
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _gate = new(1, 1);

    // Replaced as a whole, so lock-free readers never see a result with another's timestamp.
    private volatile Snapshot? _last;

    public async Task<HealthCheckResult> GetAsync(Func<CancellationToken, Task<HealthCheckResult>> probe, CancellationToken cancellationToken)
    {
        if (TryGetFresh(out var cached))
        {
            return cached;
        }

        // A waiting request may give up; the request that runs the probe waits for it
        // (bounded by ProbeTimeout) so the shared result is always a real one.
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Another request may have refreshed the result while this one waited.
            if (TryGetFresh(out cached))
            {
                return cached;
            }

            using var timeout = new CancellationTokenSource(ProbeTimeout, time);
            var result = await probe(timeout.Token);
            _last = new Snapshot(result, time.GetTimestamp());
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool TryGetFresh(out HealthCheckResult result)
    {
        var last = _last;
        result = last?.Result ?? default;
        return last is not null && time.GetElapsedTime(last.At) < MaxAge;
    }

    public void Dispose() => _gate.Dispose();

    private sealed record Snapshot(HealthCheckResult Result, long At);
}

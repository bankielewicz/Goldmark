using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Goldmark.Api.Health;

/// <summary>
/// Bounds the cost of the anonymous readiness probe: concurrent requests share one in-flight
/// check, and a result is reused for <see cref="MaxAge"/>. However many requests arrive, SQL
/// Server sees at most one probe connection per <see cref="MaxAge"/>.
/// </summary>
internal sealed class ReadinessGate(TimeProvider time) : IDisposable
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private HealthCheckResult _last;
    private DateTimeOffset _lastAt = DateTimeOffset.MinValue;

    public async Task<HealthCheckResult> GetAsync(Func<CancellationToken, Task<HealthCheckResult>> probe, CancellationToken cancellationToken)
    {
        if (TryGetFresh(out var cached))
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Another request may have refreshed the result while this one waited.
            if (TryGetFresh(out cached))
            {
                return cached;
            }

            var result = await probe(cancellationToken);
            _last = result;
            _lastAt = time.GetUtcNow();
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool TryGetFresh(out HealthCheckResult result)
    {
        result = _last;
        return time.GetUtcNow() - _lastAt < MaxAge;
    }

    public void Dispose() => _gate.Dispose();
}

using Goldmark.Api.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Goldmark.Api.Tests;

/// <summary>The anonymous readiness probe must not turn request floods into SQL connection floods.</summary>
public sealed class ReadinessGateTests
{
    private sealed class ManualTime(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task Concurrent_requests_share_one_probe()
    {
        var time = new ManualTime(DateTimeOffset.UnixEpoch);
        using var gate = new ReadinessGate(time);
        var probes = 0;
        var release = new TaskCompletionSource();
        async Task<HealthCheckResult> Probe(CancellationToken ct)
        {
            Interlocked.Increment(ref probes);
            await release.Task;
            return HealthCheckResult.Healthy();
        }

        var requests = Enumerable.Range(0, 50).Select(_ => gate.GetAsync(Probe, TestContext.Current.CancellationToken)).ToList();
        release.SetResult();
        var results = await Task.WhenAll(requests);

        Assert.Equal(1, probes);
        Assert.All(results, r => Assert.Equal(HealthStatus.Healthy, r.Status));
    }

    [Fact]
    public async Task Results_are_reused_briefly_then_refreshed()
    {
        var time = new ManualTime(DateTimeOffset.UnixEpoch);
        using var gate = new ReadinessGate(time);
        var probes = 0;
        Task<HealthCheckResult> Probe(CancellationToken ct)
        {
            probes++;
            return Task.FromResult(probes == 1 ? HealthCheckResult.Unhealthy() : HealthCheckResult.Healthy());
        }

        Assert.Equal(HealthStatus.Unhealthy, (await gate.GetAsync(Probe, TestContext.Current.CancellationToken)).Status);
        time.Now += ReadinessGate.MaxAge - TimeSpan.FromMilliseconds(1);
        Assert.Equal(HealthStatus.Unhealthy, (await gate.GetAsync(Probe, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(1, probes);

        time.Now += TimeSpan.FromMilliseconds(2);
        Assert.Equal(HealthStatus.Healthy, (await gate.GetAsync(Probe, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(2, probes);
    }
}

using Goldmark.Api.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Goldmark.Api.Tests;

/// <summary>The anonymous readiness probe must not turn request floods into SQL connection floods.</summary>
public sealed class ReadinessGateTests
{
    /// <summary>Wall clock and monotonic clock move independently, as on a real host.</summary>
    private sealed class ManualTime(DateTimeOffset start) : TimeProvider
    {
        private TimeSpan _monotonic = TimeSpan.FromHours(1);

        public DateTimeOffset Now { get; set; } = start;

        public void Advance(TimeSpan by)
        {
            Now += by;
            _monotonic += by;
        }

        public override DateTimeOffset GetUtcNow() => Now;

        public override long GetTimestamp() => _monotonic.Ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
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
        time.Advance(ReadinessGate.MaxAge - TimeSpan.FromMilliseconds(1));
        Assert.Equal(HealthStatus.Unhealthy, (await gate.GetAsync(Probe, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(1, probes);

        time.Advance(TimeSpan.FromMilliseconds(2));
        Assert.Equal(HealthStatus.Healthy, (await gate.GetAsync(Probe, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(2, probes);
    }

    [Fact]
    public async Task A_client_that_disconnects_cannot_cancel_or_poison_the_shared_probe()
    {
        using var gate = new ReadinessGate(new ManualTime(DateTimeOffset.UnixEpoch));
        var release = new TaskCompletionSource();
        var probeToken = CancellationToken.None;
        async Task<HealthCheckResult> Probe(CancellationToken ct)
        {
            probeToken = ct;
            await release.Task;
            return ct.IsCancellationRequested ? HealthCheckResult.Unhealthy("cancelled") : HealthCheckResult.Healthy();
        }

        using var aborted = new CancellationTokenSource();
        var first = gate.GetAsync(Probe, aborted.Token);
        await aborted.CancelAsync();
        release.SetResult();

        Assert.Equal(HealthStatus.Healthy, (await first).Status);
        Assert.False(probeToken.IsCancellationRequested);
        // The cached result other callers get is the real one, not the cancellation.
        Assert.Equal(HealthStatus.Healthy, (await gate.GetAsync(Probe, TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task A_wall_clock_moving_backward_does_not_extend_the_cache()
    {
        var time = new ManualTime(DateTimeOffset.UnixEpoch.AddDays(1));
        using var gate = new ReadinessGate(time);
        var probes = 0;
        Task<HealthCheckResult> Probe(CancellationToken ct) =>
            Task.FromResult(++probes == 1 ? HealthCheckResult.Unhealthy() : HealthCheckResult.Healthy());

        Assert.Equal(HealthStatus.Unhealthy, (await gate.GetAsync(Probe, TestContext.Current.CancellationToken)).Status);
        time.Advance(ReadinessGate.MaxAge + TimeSpan.FromSeconds(1));
        time.Now -= TimeSpan.FromHours(1); // e.g. a VM clock correction

        Assert.Equal(HealthStatus.Healthy, (await gate.GetAsync(Probe, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(2, probes);
    }
}

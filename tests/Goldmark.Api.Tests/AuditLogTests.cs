using System.Reflection;
using System.Text.Json;
using Goldmark.Api.Audit;
using Goldmark.Api.Commands;
using Goldmark.Api.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;

namespace Goldmark.Api.Tests;

/// <summary>Screen 16's catalog, filters and export, the retention setting and the purge job's schedule (US-07, DEC-041).</summary>
public sealed class AuditLogTests
{
    [Fact]
    public void Every_audit_action_is_in_the_catalog_once_with_a_known_outcome()
    {
        var constants = typeof(AuditActions).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();

        Assert.Equal(constants.Order(StringComparer.Ordinal), AuditCatalog.Actions.Select(a => a.Code).Order(StringComparer.Ordinal));
        Assert.All(AuditCatalog.Actions, a => Assert.Contains(a.Outcome, AuditCatalog.Outcomes));
        Assert.Equal(AuditCatalog.Outcomes.Order(), AuditCatalog.Actions.Select(a => a.Outcome).Distinct().Order()); // each outcome is used
    }

    [Fact]
    public void The_actions_written_in_SQL_match_their_constants()
    {
        var purge = File.ReadAllText(Path.Combine(SchemaMigrationsTests.RepositoryRoot(), "db", "migrations", "0004_audit_retention.sql"));

        Assert.Contains($"'{AuditActions.RetentionPurged}'", purge, StringComparison.Ordinal);
        Assert.Contains($"'{AuditActions.RetentionChanged}'", MigrateCommand.ApplyRetentionSql, StringComparison.Ordinal);
        Assert.Contains($"('System', '{AuditActions.RetentionPurged}'", purge, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(365, true)]
    [InlineData(3650, true)]
    [InlineData(36500, true)]
    [InlineData(364, false)]
    [InlineData(1, false)]
    [InlineData(-1, false)]
    [InlineData(36501, false)]
    public void Retention_is_at_least_365_days_or_indefinite(int days, bool valid)
    {
        var errors = AuditOptions.Errors(new AuditOptions { RetentionDays = days });

        Assert.Equal(valid, errors.Count == 0);
        Assert.All(errors, e => Assert.StartsWith("Goldmark:Audit:RetentionDays must be", e, StringComparison.Ordinal));
        Assert.Equal(365, new AuditOptions().RetentionDays);
    }

    [Fact]
    public void A_retention_period_that_is_not_a_number_is_named_without_its_value()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Goldmark:Audit:RetentionDays"] = "forever-ish" }).Build();

        var error = Assert.Single(ConfigurationPreflight.BindingErrors(configuration));

        Assert.Contains("Goldmark:Audit", error, StringComparison.Ordinal);
        Assert.DoesNotContain("forever-ish", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Filters_parse_into_parameters_and_the_outcome_narrows_the_actions()
    {
        var actor = Guid.NewGuid();
        var (query, problem) = AuditQuery.Parse(Query(
            ("from", "2026-10-13T00:00:00Z"), ("to", "2026-10-20T08:00:00+02:00"), ("actor", actor.ToString("D")),
            ("action", AuditActions.SignInFailed), ("action", AuditActions.SignInSucceeded), ("outcome", AuditCatalog.Failed),
            ("ip", "10.20.4."), ("order", "oldest")));

        Assert.Null(problem);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 6, 0, 0, TimeSpan.Zero), query!.To);
        Assert.Equal(TimeSpan.Zero, query.To!.Value.Offset);
        Assert.Equal((AuditActorTypes.User, actor), (query.ActorType, query.ActorId));
        Assert.Equal([AuditActions.SignInFailed], JsonSerializer.Deserialize<string[]>(query.ActionsJson()!)!);
        Assert.True(query.Oldest);

        var filters = query.Describe();
        Assert.Equal(["from", "to", "actor", "actions", "outcome", "ip", "order"], filters.Keys);
        Assert.Null(AuditQuery.None.ActionsJson());
        Assert.Equal(["order"], AuditQuery.None.Describe().Keys);
    }

    [Theory]
    [InlineData("System", "System")]
    [InlineData("Anonymous", "Anonymous")]
    public void The_actor_filter_takes_a_person_or_an_actor_type(string value, string type)
    {
        var (query, _) = AuditQuery.Parse(Query(("actor", value)));

        Assert.Equal(type, query!.ActorType);
        Assert.Null(query.ActorId);
    }

    [Theory]
    [InlineData("ip", "10.20.%")]
    [InlineData("ip", "10.20.4_")]
    [InlineData("ip", "10.20.4.17' OR 1=1")]
    [InlineData("action", "auth.sign-in.failed' OR 1=1")]
    [InlineData("actor", "user")]
    [InlineData("outcome", "failed")]
    [InlineData("from", "yesterday")]
    [InlineData("from", "1753-01-01T00:00:00Z")]
    [InlineData("order", "random")]
    public void A_value_that_could_carry_SQL_or_a_wildcard_is_refused(string name, string value)
    {
        var (query, problem) = AuditQuery.Parse(Query((name, value)));

        Assert.Null(query);
        Assert.NotNull(problem);
    }

    [Theory]
    [InlineData("limit", "0")]
    [InlineData("limit", "501")]
    [InlineData("limit", "ten")]
    [InlineData("cursor", "0")]
    [InlineData("cursor", "-1")]
    public void Page_parameters_are_bounded(string name, string value)
    {
        var (_, _, problem) = AuditQuery.Page(Query((name, value)));

        Assert.NotNull(problem);
        Assert.Equal((AuditQuery.DefaultLimit, (long?)null, (IResult?)null), AuditQuery.Page(Query()));
    }

    [Fact]
    public void The_export_neutralizes_formula_cells_and_names_removed_accounts()
    {
        using var details = JsonDocument.Parse("""{"userName":"=cmd|' /C calc'!A0"}""");
        var entries = new[]
        {
            new AuditEntry(7, new DateTime(2026, 10, 20, 7, 58, 12, DateTimeKind.Utc), new AuditParty("User", Guid.NewGuid(), "dana.k", "=HYPERLINK(\"x\")", false),
                AuditActions.InvitationRevoked, "Invitation revoked", AuditCatalog.Succeeded,
                new AuditParty("User", Guid.NewGuid(), "=cmd|' /C calc'!A0", null, true), "10.20.4.11", details.RootElement.Clone()),
            new AuditEntry(8, new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc), new AuditParty("System", null, null, null, false),
                AuditActions.RetentionPurged, "Old entries deleted", AuditCatalog.Succeeded, null, null, null),
        };

        var lines = AuditLog.ToCsv(entries).Split("\r\n");

        Assert.Equal("2026-10-20 07:58:12,Person,\"'=HYPERLINK(\"\"x\"\") (dana.k)\",Invitation revoked,user.invitation.revoked,\"'=cmd|' /C calc'!A0 (account removed)\",10.20.4.11,Succeeded,"
            + "\"{\"\"userName\"\":\"\"=cmd|' /C calc'!A0\"\"}\"", lines[1]);
        Assert.Equal("2026-10-20 08:00:00,System,,Old entries deleted,audit.retention.purged,,,Succeeded,", lines[2]);
    }

    [Fact]
    public async Task The_purge_job_waits_a_minute_then_retries_until_the_database_is_ready()
    {
        var time = new ManualTimers(DateTimeOffset.UnixEpoch);
        var health = new CountingHealthChecks(HealthStatus.Unhealthy);
        await using var services = new ServiceCollection().BuildServiceProvider();
        using var job = new AuditRetentionJob(services.GetRequiredService<IServiceScopeFactory>(), health, time, NullLogger<AuditRetentionJob>.Instance);

        await job.StartAsync(TestContext.Current.CancellationToken);
        await time.WaitForTimerAsync();
        time.Advance(AuditRetentionJob.StartDelay - TimeSpan.FromSeconds(1));
        Assert.Equal(0, health.Calls); // in-process test hosts never reach the database

        time.Advance(TimeSpan.FromSeconds(1));
        await health.WaitForAsync(1);
        await time.WaitForTimerAsync();
        time.Advance(AuditRetentionJob.RetryInterval);
        await health.WaitForAsync(2);

        await job.StopAsync(TestContext.Current.CancellationToken);
        Assert.True(job.ExecuteTask!.IsCompletedSuccessfully);
    }

    private static QueryCollection Query(params (string Name, string Value)[] values) =>
        new(values.GroupBy(v => v.Name).ToDictionary(g => g.Key, g => new StringValues([.. g.Select(v => v.Value)])));

    private sealed class CountingHealthChecks(HealthStatus status) : HealthCheckService
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public override Task<HealthReport> CheckHealthAsync(Func<HealthCheckRegistration, bool>? predicate, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new HealthReport(new Dictionary<string, HealthReportEntry>(), status, TimeSpan.Zero));
        }

        public async Task WaitForAsync(int calls)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (Calls < calls)
            {
                await Task.Delay(10, timeout.Token);
            }
        }
    }

    /// <summary>A clock whose timers fire only when the test advances it.</summary>
    private sealed class ManualTimers(DateTimeOffset start) : TimeProvider
    {
        private readonly Lock _lock = new();
        private readonly List<Timer> _timers = [];
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_lock)
            {
                return _now;
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(this, callback, state);
            lock (_lock)
            {
                _timers.Add(timer);
                timer.Due = dueTime == Timeout.InfiniteTimeSpan ? null : _now + dueTime;
            }

            return timer;
        }

        public void Advance(TimeSpan by)
        {
            List<Timer> due;
            lock (_lock)
            {
                _now += by;
                due = [.. _timers.Where(t => t.Due <= _now)];
                due.ForEach(t => t.Due = null);
            }

            due.ForEach(t => t.Fire());
        }

        /// <summary>Waits until some timer is pending (the job has started its next wait).</summary>
        public async Task WaitForTimerAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (true)
            {
                lock (_lock)
                {
                    if (_timers.Any(t => t.Due is not null))
                    {
                        return;
                    }
                }

                await Task.Delay(10, timeout.Token);
            }
        }

        private sealed class Timer(ManualTimers owner, TimerCallback callback, object? state) : ITimer
        {
            public DateTimeOffset? Due { get; set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner._lock)
                {
                    Due = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
                }

                return true;
            }

            public void Fire() => callback(state);

            public void Dispose()
            {
                lock (owner._lock)
                {
                    Due = null;
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}

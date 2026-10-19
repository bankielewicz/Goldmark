using Goldmark.Api.Data;
using Goldmark.Api.Health;
using Goldmark.Api.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Goldmark.Api.IntegrationTests;

/// <summary>
/// Readiness requires the database schema version to equal the version this build expects,
/// logs both values on a mismatch, and is not ready for an older or a newer schema (slice 2).
/// </summary>
[Collection(SqlServerTests.Name)]
public sealed class ReadinessSchemaTests(SqlServerFixture sql)
{
    [Fact]
    public async Task Ready_when_the_schema_matches_this_build()
    {
        var (installation, _) = await sql.ProvisionAsync();
        var logger = new ListLogger<SqlReadinessCheck>();

        var result = await CheckAsync(installation, logger);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task Not_ready_before_migrations_ran_and_both_versions_are_logged()
    {
        var installation = sql.NewInstallation();
        var bootstrap = await sql.RunAsync(installation, ["bootstrap-db"]);
        Assert.True(bootstrap.Code == 0, bootstrap.ToString());
        var logger = new ListLogger<SqlReadinessCheck>();

        var result = await CheckAsync(installation, logger);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        AssertMismatchLogged(logger, databaseVersion: 0);
    }

    [Fact]
    public async Task Not_ready_when_a_newer_build_migrated_the_database()
    {
        var (installation, _) = await sql.ProvisionAsync();
        await sql.AsAdminAsync(installation.Database,
            "INSERT INTO dbo.SchemaVersions (ScriptName, Applied) VALUES (N'Goldmark.Migrations.9999_future.sql', SYSUTCDATETIME());");
        var logger = new ListLogger<SqlReadinessCheck>();

        var result = await CheckAsync(installation, logger);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        AssertMismatchLogged(logger, databaseVersion: 9999);
    }

    private async Task<HealthCheckResult> CheckAsync(Installation installation, ListLogger<SqlReadinessCheck> logger)
    {
        using var gate = new ReadinessGate(TimeProvider.System);
        var check = new SqlReadinessCheck(new SqlConnectionFactory(Options.Create(sql.DatabaseOptions(installation))), gate, logger);
        return await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);
    }

    private static void AssertMismatchLogged(ListLogger<SqlReadinessCheck> logger, int databaseVersion)
    {
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(1003, entry.EventId.Id);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(databaseVersion, entry.Value("DatabaseSchemaVersion"));
        Assert.Equal(SchemaMigrations.ExpectedVersion, entry.Value("ExpectedSchemaVersion"));
    }
}

internal sealed class ListLogger<T> : ILogger<T>
{
    public List<Entry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        // Debug detail of other failures is not part of these assertions.
        if (logLevel >= LogLevel.Information)
        {
            Entries.Add(new Entry(logLevel, eventId, state as IReadOnlyList<KeyValuePair<string, object?>> ?? []));
        }
    }

    public sealed record Entry(LogLevel Level, EventId EventId, IReadOnlyList<KeyValuePair<string, object?>> State)
    {
        public object? Value(string name) => State.Single(p => p.Key == name).Value;
    }
}

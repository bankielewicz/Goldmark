using Goldmark.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Goldmark.Api.Health;

/// <summary>
/// Readiness: opens a fresh (unpooled) connection with the least-privilege runtime login and
/// reads the migration journal. Ready only when the database's schema version equals the
/// version this build expects (DEC-023): an older schema (migrations not yet applied) and a
/// newer one (a newer build migrated it) are both not ready, and both versions are logged.
/// Failure details are logged (exception text only at Debug) and never returned to the caller. With
/// <c>Goldmark:Database:CheckRightsAtStart</c>, also not ready until <see cref="RuntimeRightsCheck"/> passed.
/// </summary>
internal sealed partial class SqlReadinessCheck(
    SqlConnectionFactory connections, ReadinessGate gate, RuntimeRights rights, ILogger<SqlReadinessCheck> logger)
    : IHealthCheck
{
    public const string Name = "sql";

    // The endpoint is anonymous; the gate coalesces concurrent probes and caches the result
    // briefly so request floods cannot turn into SQL Server connection floods.
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        gate.GetAsync(ProbeAsync, cancellationToken);

    private async Task<HealthCheckResult> ProbeAsync(CancellationToken cancellationToken)
    {
        var builder = connections.CreateBuilder();
        builder.Pooling = false;
        try
        {
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = SchemaMigrations.AppliedScriptsSql;
            command.CommandTimeout = 5;
            var applied = new List<string>();
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    applied.Add(reader.GetString(0));
                }
            }

            var version = SchemaMigrations.DatabaseVersion(applied);
            if (version != SchemaMigrations.ExpectedVersion)
            {
                LogSchemaMismatch(logger, version, SchemaMigrations.ExpectedVersion);
                return HealthCheckResult.Unhealthy("Database schema version does not match this build.");
            }

            // With Goldmark:Database:CheckRightsAtStart, not ready until the login's rights are checked.
            return rights.Checked ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("This login's rights are not checked yet.");
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException or OperationCanceledException)
        {
            LogNotReady(logger, ex.GetType().Name, (ex as SqlException)?.Number ?? 0);
            LogNotReadyDetail(logger, ex);
            return HealthCheckResult.Unhealthy("SQL Server is not available.");
        }
    }

    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning,
        Message = "Readiness check failed: SQL Server unavailable ({ExceptionType}, SQL error {SqlErrorNumber})")]
    private static partial void LogNotReady(ILogger logger, string exceptionType, int sqlErrorNumber);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Debug, Message = "Readiness check failure detail")]
    private static partial void LogNotReadyDetail(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning,
        Message = "Readiness check failed: database schema version {DatabaseSchemaVersion} does not match the version this build expects ({ExpectedSchemaVersion}); run the migrate command (db-init) with this build")]
    private static partial void LogSchemaMismatch(ILogger logger, int databaseSchemaVersion, int expectedSchemaVersion);
}

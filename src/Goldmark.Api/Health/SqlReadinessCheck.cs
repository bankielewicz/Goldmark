using Goldmark.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Goldmark.Api.Health;

/// <summary>
/// Readiness: opens a fresh (unpooled) connection with the least-privilege runtime login and
/// executes a real query. Slice 2 extends this with a schema-version compatibility check.
/// Failure details are logged (exception text only at Debug) and never returned to the caller.
/// </summary>
internal sealed partial class SqlReadinessCheck(SqlConnectionFactory connections, ILogger<SqlReadinessCheck> logger)
    : IHealthCheck
{
    public const string Name = "sql";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var builder = connections.CreateBuilder();
        builder.Pooling = false;
        try
        {
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1;";
            command.CommandTimeout = 5;
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is 1
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Unexpected result from readiness query.");
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
}

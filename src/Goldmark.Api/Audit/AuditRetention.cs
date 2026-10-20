using Goldmark.Api.Data;
using Goldmark.Api.Health;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Goldmark.Api.Audit;

/// <summary>
/// The audit log's retention (NFR-LOG-3, DEC-041): the period <c>migrate</c> wrote to
/// <c>cmdb.AuditRetention</c>, and the purge. The runtime login only starts <c>cmdb.PurgeAuditLog</c>,
/// which takes no parameters and deletes what that period allows, never anything younger than 365 days.
/// </summary>
internal sealed class AuditRetention(DbSession db)
{
    /// <summary>Generous: a first purge of a large log deletes in many batches, each committed on its own.</summary>
    private const int PurgeTimeoutSeconds = 600;

    /// <summary>The retention period in days; 0 keeps entries indefinitely.</summary>
    public async Task<int> PeriodDaysAsync(CancellationToken cancellationToken = default) =>
        await db.ExecuteScalarAsync<int?>("SELECT RetentionDays FROM cmdb.AuditRetention WHERE Id = 1;", cancellationToken: cancellationToken) ?? 0;

    /// <summary>
    /// Runs the purge. Never inside a transaction: the procedure commits each batch with its audit entry,
    /// and refuses to run inside a caller's transaction.
    /// </summary>
    public async Task<PurgeResult> PurgeAsync(CancellationToken cancellationToken)
    {
        if (db.InTransaction)
        {
            throw new InvalidOperationException("The audit purge commits its own batches; call it outside a transaction.");
        }

        return await db.QuerySingleOrDefaultAsync<PurgeResult>("EXEC cmdb.PurgeAuditLog;", PurgeTimeoutSeconds, cancellationToken)
            ?? new PurgeResult(0, null, 0);
    }
}

internal sealed record PurgeResult(int Deleted, DateTime? Before, int RetentionDays);

/// <summary>
/// Runs the audit purge daily (DEC-041). The first run waits a minute after start, and every run waits
/// for readiness (the database reachable and at this build's schema version), so a host whose database
/// is missing or not yet migrated only logs that it will try again.
/// </summary>
internal sealed partial class AuditRetentionJob(
    IServiceScopeFactory scopes, HealthCheckService health, TimeProvider time, ILogger<AuditRetentionJob> logger) : BackgroundService
{
    public static readonly TimeSpan StartDelay = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    public static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartDelay, time, stoppingToken);
            while (true)
            {
                await Task.Delay(await RunOnceAsync(stoppingToken) ? Interval : RetryInterval, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping.
        }
    }

    /// <summary>One run; true when the purge ran (whether or not it deleted anything).</summary>
    internal async Task<bool> RunOnceAsync(CancellationToken cancellationToken)
    {
        var readiness = await health.CheckHealthAsync(c => c.Tags.Contains(HealthEndpoints.ReadyTag), cancellationToken);
        if (readiness.Status != HealthStatus.Healthy)
        {
            LogNotReady(logger, RetryInterval.TotalMinutes);
            return false;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<AuditRetention>().PurgeAsync(cancellationToken);
            if (result.RetentionDays == 0)
            {
                LogKeptIndefinitely(logger);
            }
            else
            {
                LogPurged(logger, result.Deleted, result.Before, result.RetentionDays);
            }

            return true;
        }
        catch (SqlException ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogFailed(logger, ex.Number, RetryInterval.TotalMinutes);
            return false;
        }
    }

    [LoggerMessage(EventId = 4001, Level = LogLevel.Information,
        Message = "Audit retention: deleted {Deleted} entries from before {Before:yyyy-MM-dd} UTC (retention {RetentionDays} days)")]
    private static partial void LogPurged(ILogger logger, int deleted, DateTime? before, int retentionDays);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Information, Message = "Audit retention: entries are kept indefinitely")]
    private static partial void LogKeptIndefinitely(ILogger logger);

    [LoggerMessage(EventId = 4003, Level = LogLevel.Debug, Message = "Audit retention: the database is not ready; trying again in {RetryMinutes} minutes")]
    private static partial void LogNotReady(ILogger logger, double retryMinutes);

    [LoggerMessage(EventId = 4004, Level = LogLevel.Warning, Message = "Audit retention: the purge failed (SQL error {SqlErrorNumber}); trying again in {RetryMinutes} minutes")]
    private static partial void LogFailed(ILogger logger, int sqlErrorNumber, double retryMinutes);
}

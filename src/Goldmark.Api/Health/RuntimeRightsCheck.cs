using Goldmark.Api.Commands;
using Goldmark.Api.Configuration;
using Goldmark.Api.Data;
using Goldmark.Api.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Goldmark.Api.Health;

/// <summary>Whether the application's own rights in SQL Server have been checked (see <see cref="RuntimeRightsCheck"/>).</summary>
internal sealed class RuntimeRights
{
    private volatile bool _checked;
    private volatile int _exitCode;

    public bool Checked => _checked;

    /// <summary>The process's exit code: <see cref="RuntimeRightsCheck.Refused"/> after a refusal, else 0.</summary>
    public int ExitCode => _exitCode;

    public void MarkChecked() => _checked = true;

    public void Refuse() => _exitCode = RuntimeRightsCheck.Refused;
}

/// <summary>
/// With <c>Goldmark:Database:CheckRightsAtStart</c> (the Windows service, DEC-043 items 2 and 11), the
/// application checks at each start, as its own login, what <c>migrate</c> checks as the runtime login on
/// Docker: it holds exactly <c>goldmark_runtime</c>'s rights, and the principal audit's effective-rights
/// layer finds nothing (accepted findings: <c>Goldmark:Bootstrap:AcceptedFindings</c>). Only the service's
/// own token shows rights it gets through Windows groups; an administrator's impersonation does not. Readiness
/// fails until the check passes; it waits for SQL Server and the expected schema version. If the login holds
/// more, Goldmark logs why and stops (exit code 3), and as a Windows service writes the reason to the
/// Application log.
/// </summary>
internal sealed partial class RuntimeRightsCheck(
    IOptions<DatabaseOptions> database,
    SqlConnectionFactory connections,
    RuntimeRights rights,
    IConfiguration configuration,
    IHostApplicationLifetime lifetime,
    IHostLifetime hostLifetime,
    ILogger<RuntimeRightsCheck> logger) : BackgroundService
{
    public const int Refused = 3;
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!database.Value.CheckRightsAtStart)
        {
            rights.MarkChecked();
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var refused = await CheckAsync(stoppingToken);
                if (refused is null)
                {
                    // Not yet: SQL Server or the expected schema version isn't there.
                    await Task.Delay(Retry, stoppingToken);
                    continue;
                }

                if (refused.Count == 0)
                {
                    rights.MarkChecked();
                    LogChecked(logger);
                    return;
                }

                var reason = $"Goldmark stopped: its login in SQL Server holds more than {BootstrapDatabaseCommand.RuntimeRole}: {string.Join("; ", refused)}";
                LogRefused(logger, reason);
                if (OperatingSystem.IsWindows() && WindowsService.IsRunning)
                {
                    WindowsService.WriteEvent(reason, WindowsService.RefusedEvent);
                    if (hostLifetime is System.ServiceProcess.ServiceBase service)
                    {
                        // Windows records the stop as a failure, as for a refused start; it doesn't restart it.
                        service.ExitCode = WindowsService.RefusedExitCode;
                    }
                }

                rights.Refuse();
                lifetime.StopApplication();
                return;
            }
            catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException)
            {
                LogWaiting(logger, ex.GetType().Name, (ex as SqlException)?.Number ?? 0);
                await Task.Delay(Retry, stoppingToken);
            }
        }
    }

    /// <summary>The refusals, empty when the rights are exactly right, or null when the database isn't ready to check.</summary>
    private async Task<List<string>?> CheckAsync(CancellationToken cancellationToken)
    {
        var builder = connections.CreateBuilder();
        builder.Pooling = false;
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        if (SchemaMigrations.DatabaseVersion(await MigrateCommand.AppliedScriptsAsync(connection)) != SchemaMigrations.ExpectedVersion)
        {
            return null;
        }

        var refused = await CommandSql.FindingsAsync(connection, MigrateCommand.VerifyRuntimeSql);
        var policy = FindingPolicy.Parse(configuration[FindingPolicy.Setting]);
        // Other databases too: a user there for one of this login's Windows groups shows only in its own token
        // (bootstrap-db's pass, by impersonation, can't see it; Codex on #39).
        var findings = policy.Refused(await PrincipalAudit.SelfAsync(connection, database.Value.Name, runtime: true, databases: true), TextWriter.Null, "start");
        refused.AddRange(findings.Select(f => $"[{f.Id}] {f.Description}"));
        return refused;
    }

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information, Message = "Checked this login's rights in SQL Server: exactly goldmark_runtime's")]
    private static partial void LogChecked(ILogger logger);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Error, Message = "{Reason}")]
    private static partial void LogRefused(ILogger logger, string reason);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Warning,
        Message = "Waiting to check this login's rights in SQL Server ({ExceptionType}, SQL error {SqlErrorNumber})")]
    private static partial void LogWaiting(ILogger logger, string exceptionType, int sqlErrorNumber);
}

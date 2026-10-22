using Goldmark.Api.Audit;
using Goldmark.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Goldmark.Api.Auth;

/// <summary>
/// The way back in when no administrator can sign in (issue #25; DEC-042 item 6). While
/// <see cref="AuthOptions.RecoverAdmin"/> names an enabled Admin who accepted their invitation and is
/// not locked by an administrator, each start issues a password-and-two-factor link for them, with no
/// issuing account, and logs its path and code once at Warning under its own event ID, as the setup
/// token is logged. The link replaces any open link of theirs and changes nothing on the account until
/// it is used; using it ends every session and sets a new password and authenticator. With the setting
/// naming anyone else, a warning says so and no link is made. The recovery link and the setup token are
/// the two secrets Goldmark ever logs.
/// </summary>
internal sealed partial class RecoverAdminService(IServiceScopeFactory scopes, IOptions<AuthOptions> options, ILogger<RecoverAdminService> logger)
    : BackgroundService
{
    public static readonly EventId RecoveryLinkEvent = new(3004, "RecoveryLink");

    private static readonly TimeSpan MaximumDelay = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var name = options.Value.RecoverAdmin.Trim();
        if (name.Length == 0)
        {
            return;
        }

        var delay = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<DbSession>();
                // The link and its audit entry need this build's schema: until db-init has run, wait.
                if (SchemaMigrations.DatabaseVersion(await db.QueryAsync<string>(SchemaMigrations.AppliedScriptsSql, cancellationToken: stoppingToken))
                    == SchemaMigrations.ExpectedVersion)
                {
                    await IssueAsync(scope.ServiceProvider, db, name);
                    return;
                }

                LogWaiting(logger, 0);
            }
            catch (SqlException ex)
            {
                // Not reachable yet: try again later.
                LogWaiting(logger, ex.Number);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Anything else (a pool timeout, a socket error) must not stop the host either.
                LogWaiting(logger, ex.HResult);
            }

            await Task.Delay(delay, stoppingToken);
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaximumDelay.Ticks));
        }
    }

    private async Task IssueAsync(IServiceProvider services, DbSession db, string name)
    {
        var users = services.GetRequiredService<GoldmarkUserManager>();
        var user = await users.FindByNameAsync(name);
        if (user is null || user.IsDisabled || user.IsPending || !await users.IsInRoleAsync(user, Roles.Admin))
        {
            LogNotAnAdmin(logger);
            return;
        }

        if (user.IsLockedByAdministrator)
        {
            LogLockedByAdministrator(logger);
            return;
        }

        var links = services.GetRequiredService<LinkStore>();
        var audit = services.GetRequiredService<AuditTrail>();
        // No HTTP request: the audit entry has no address, and its actor is System.
        var link = await db.InTransactionAsync(async () =>
        {
            var issued = await links.IssueAsync(user.Id, LinkPurpose.CombinedReset, createdBy: null);
            await audit.WriteAsync(AuditActions.PasswordReset, AuditActor.System, "User", user.Id,
                new { userName = user.UserName, twoFactorToo = true, issuedBy = LinkIssuers.ServerSetting, expiresAt = issued.ExpiresAt });
            return issued;
        });
        LogRecoveryLink(logger, user.Id, $"/password-reset#t={link.Token}", link.Code, link.ExpiresAt, link.CodeAttempts);
    }

    [LoggerMessage(EventId = 3004, EventName = "RecoveryLink", Level = LogLevel.Warning,
        Message = "Recovery link for the Admin that the setting GOLDMARK_RECOVER_ADMIN names (user {UserId}): in a browser, open {RecoveryPath} on the " +
            "address people use for Goldmark, then enter the code {RecoveryCode}. It sets a new password and authenticator and ends every " +
            "session; nothing changes until then. It works until {ExpiresAt:yyyy-MM-dd HH:mm} UTC, and {CodeAttempts} wrong codes end it. " +
            "While the setting is set, each start prints a new link and the previous one stops working: remove the setting once they can sign in.")]
    private static partial void LogRecoveryLink(ILogger logger, Guid userId, string recoveryPath, string recoveryCode, DateTime expiresAt,
        int codeAttempts);

    [LoggerMessage(EventId = 3005, EventName = "RecoveryRefused", Level = LogLevel.Warning,
        Message = "The setting GOLDMARK_RECOVER_ADMIN does not name an enabled Admin who has accepted their invitation, so no recovery link " +
            "was made. Name one, or remove the setting.")]
    private static partial void LogNotAnAdmin(ILogger logger);

    [LoggerMessage(EventId = 3006, EventName = "RecoveryRefusedLocked", Level = LogLevel.Warning,
        Message = "The setting GOLDMARK_RECOVER_ADMIN names an Admin whom an administrator has locked, so no recovery link was made: an " +
            "administrator's lock stays until another Admin unlocks them. Name another Admin, or remove the setting.")]
    private static partial void LogLockedByAdministrator(ILogger logger);

    [LoggerMessage(EventId = 3007, Level = LogLevel.Debug, Message = "Recovery link: database not ready yet (error {Error}); retrying.")]
    private static partial void LogWaiting(ILogger logger, int error);
}

/// <summary>Who issued a link that no account issued, as its audit entries name it.</summary>
internal static class LinkIssuers
{
    /// <summary>The recovery setting (<see cref="RecoverAdminService"/>): an operator set it on the server.</summary>
    public const string ServerSetting = "server-setting";
}

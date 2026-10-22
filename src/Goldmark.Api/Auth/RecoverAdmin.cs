using Goldmark.Api.Audit;
using Goldmark.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Goldmark.Api.Auth;

/// <summary>
/// The way back in when no administrator can sign in (issue #25; DEC-042 item 6). While
/// <see cref="AuthOptions.RecoverAdmin"/> names an enabled Admin who accepted their invitation and is
/// not locked by an administrator, each start issues a password-and-two-factor link for them, with no
/// issuing account, and logs its path once at Warning under its own event ID, as the setup token is
/// logged. Its code is the operator's <see cref="AuthOptions.RecoverAdminCode"/>, never logged, so
/// reading the log alone is not enough; the link works for an hour (owner: "A. Operator sets it in
/// .env" and "A. 1 hour, ended at a clean restart"). It replaces any open link of theirs and changes
/// nothing on the account until it is used, so it is audited "Recovery link issued", not "Password reset";
/// using it ends every session and sets a new password and authenticator. With the setting naming
/// anyone else, a warning says so and no link is made. Every
/// start first ends every unused recovery link, whoever it was for and whatever the setting now says, so
/// a link lasts at most until the next start and emptying the setting and restarting ends a copy left in
/// a log (Codex, PR #38).
/// </summary>
internal sealed partial class RecoverAdminService(IServiceScopeFactory scopes, IOptions<AuthOptions> options, ILogger<RecoverAdminService> logger)
    : BackgroundService
{
    public static readonly EventId RecoveryLinkEvent = new(3004, "RecoveryLink");

    private static readonly TimeSpan MaximumDelay = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var name = options.Value.RecoverAdmin.Trim();
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
                    await EndRecoveryLinksAsync(scope.ServiceProvider, db);
                    if (name.Length > 0)
                    {
                        await IssueAsync(scope.ServiceProvider, db, name);
                    }

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

        // The validator has checked the code's shape (AuthOptionsValidator).
        var code = LinkSecrets.NormalizeRecoveryCode(options.Value.RecoverAdminCode)!;
        var links = services.GetRequiredService<LinkStore>();
        var audit = services.GetRequiredService<AuditTrail>();
        // No HTTP request: the audit entry has no address, and its actor is System.
        var link = await db.InTransactionAsync(async () =>
        {
            var issued = await links.IssueRecoveryAsync(user.Id, code);
            // Nothing changes until the link is used: then "Password reset completed" records the reset.
            await audit.WriteAsync(AuditActions.RecoveryLinkIssued, AuditActor.System, "User", user.Id,
                new { userName = user.UserName, expiresAt = issued.ExpiresAt });
            return issued;
        });
        LogRecoveryLink(logger, user.Id, $"/password-reset#t={link.Token}", link.ExpiresAt, link.CodeAttempts);
    }

    private static async Task EndRecoveryLinksAsync(IServiceProvider services, DbSession db)
    {
        var links = services.GetRequiredService<LinkStore>();
        var audit = services.GetRequiredService<AuditTrail>();
        await db.InTransactionAsync(async () =>
        {
            foreach (var userId in await links.RevokeRecoveryLinksAsync())
            {
                await audit.WriteAsync(AuditActions.LinkInvalidated, AuditActor.System, "User", userId,
                    new { purpose = LinkPurpose.CombinedReset, reason = "restart" });
            }

            return 0;
        });
    }

    [LoggerMessage(EventId = 3004, EventName = "RecoveryLink", Level = LogLevel.Warning,
        Message = "Recovery link for the Admin that the setting GOLDMARK_RECOVER_ADMIN names (user {UserId}): in a browser, open {RecoveryPath} " +
            "on the address people use for Goldmark, then enter the code set in GOLDMARK_RECOVER_CODE (it is never logged). It sets a new " +
            "password and authenticator and ends every session; nothing changes until then. It works until {ExpiresAt:yyyy-MM-dd HH:mm} UTC, " +
            "and {CodeAttempts} wrong codes end it. Each start prints a new link while the setting is set: once they can sign in, empty both " +
            "settings and restart, which also ends this link.")]
    private static partial void LogRecoveryLink(ILogger logger, Guid userId, string recoveryPath, DateTime expiresAt, int codeAttempts);

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

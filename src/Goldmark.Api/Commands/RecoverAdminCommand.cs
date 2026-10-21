using System.Security.Cryptography;
using Goldmark.Api.Audit;
using Goldmark.Api.Auth;
using Goldmark.Api.Configuration;
using Goldmark.Api.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Goldmark.Api.Commands;

/// <summary>
/// <c>recover-admin &lt;username&gt;</c> (issue #25; DEC-042): the way back in when no administrator can sign
/// in. Run where the app runs (<c>docker compose exec app dotnet /app/Goldmark.Api.dll recover-admin dana</c>),
/// with the app's own runtime login, by an operator who can already read that login's credentials and
/// the key ring. For an enabled Admin it does what an administrator's password-and-two-factor reset
/// does, at once: the password and authenticator stop working, recovery codes go, every session ends,
/// any lock is lifted, and a link and its separate code are made. Both are printed for the operator,
/// never logged; the reset is audited with actor System.
/// Exit codes: 0 done, 1 SQL or connection error, 2 invalid usage or configuration, 3 refused.
/// </summary>
internal static class RecoverAdminCommand
{
    public const string Name = "recover-admin";

    private const string FindSql = """
        SELECT u.Id, u.UserName, u.DisplayName, u.IsDisabled,
            CAST(CASE WHEN u.PasswordHash IS NULL THEN 1 ELSE 0 END AS bit) AS IsPending,
            CAST(CASE WHEN EXISTS (SELECT 1 FROM cmdb.UserRole AS r WHERE r.UserId = u.Id AND r.Role = 'Admin') THEN 1 ELSE 0 END AS bit) AS IsAdmin
        FROM cmdb.UserAccount AS u
        WHERE u.NormalizedUserName = @name;
        """;

    private const string ResetSql = """
        UPDATE cmdb.UserAccount
        SET PasswordResetAt = @now, AuthenticatorKey = NULL, AuthenticatorSetUpAt = NULL, LastTotpTimeStep = NULL,
            AccessFailedCount = 0, LockoutEnd = NULL, SecurityStamp = @securityStamp, ConcurrencyStamp = @concurrencyStamp
        WHERE Id = @id;
        DELETE FROM cmdb.RecoveryCode WHERE UserId = @id;
        """;

    public static async Task<int> RunAsync(IReadOnlyList<string> arguments, IConfiguration configuration, TextWriter output, TextWriter error)
    {
        if (arguments.Count != 1 || string.IsNullOrWhiteSpace(arguments[0]))
        {
            await error.WriteLineAsync($"Usage: {Name} <username> (an enabled Admin).");
            return 2;
        }

        var errors = ConfigurationPreflight.BindingErrors(configuration);
        var database = new DatabaseOptions();
        var auth = new AuthOptions();
        if (errors.Count == 0)
        {
            database = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? database;
            auth = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? auth;
            DatabaseOptionsValidator.ApplyCredentialFile(database);
            errors.AddRange(new DatabaseOptionsValidator().Validate(null, database).Failures ?? []);
            errors.AddRange(new AuthOptionsValidator().Validate(null, auth).Failures ?? []);
        }

        if (errors.Count > 0)
        {
            await error.WriteLineAsync($"{Name}: invalid configuration:");
            foreach (var e in errors)
            {
                await error.WriteLineAsync($"  - {e}");
            }

            return 2;
        }

        var time = TimeProvider.System;
        try
        {
            await using var db = new DbSession(new SqlConnectionFactory(Options.Create(database)));
            var applied = await db.QueryAsync<string>(SchemaMigrations.AppliedScriptsSql);
            if (SchemaMigrations.DatabaseVersion(applied) != SchemaMigrations.ExpectedVersion)
            {
                await error.WriteLineAsync($"{Name}: the database is not at the schema version this build expects ({SchemaMigrations.ExpectedVersion}). Run db-init first.");
                return 3;
            }

            var name = arguments[0].Trim();
            var admin = await db.QuerySingleOrDefaultAsync<Account>(FindSql, new { name = name.Normalize().ToUpperInvariant() });
            if (admin is not { IsAdmin: true, IsDisabled: false, IsPending: false })
            {
                await error.WriteLineAsync($"{Name}: '{name}' is not an enabled Admin who has accepted their invitation. Nothing was changed.");
                return 3;
            }

            // No HTTP request: the audit entry has no address, and its actor is System.
            var audit = new AuditTrail(db, new HttpContextAccessor());
            var links = new LinkStore(db, Options.Create(auth), time, new EphemeralDataProtectionProvider());
            var link = await db.InTransactionAsync(async () =>
            {
                await db.ExecuteAsync(ResetSql, new
                {
                    id = admin.Id,
                    now = time.GetUtcNow().UtcDateTime,
                    securityStamp = Convert.ToHexString(RandomNumberGenerator.GetBytes(20)),
                    concurrencyStamp = Guid.NewGuid().ToString("D"),
                });
                await SessionTicketStore.EndAllAsync(db, admin.Id);
                var issued = await links.IssueAsync(admin.Id, LinkPurpose.CombinedReset, createdBy: null);
                await audit.WriteAsync(AuditActions.PasswordReset, AuditActor.System, "User", admin.Id,
                    new { userName = admin.UserName, twoFactorToo = true, issuedBy = "server-command", expiresAt = issued.ExpiresAt });
                return issued;
            });

            await output.WriteLineAsync($"{Name}: {admin.DisplayName} ({admin.UserName}) can now set a new password and authenticator.");
            await output.WriteLineAsync("Their old password and authenticator no longer work, and every session has ended.");
            await output.WriteLineAsync("Open this path on your Goldmark address, for example https://goldmark.corp.example/password-reset#t=...:");
            await output.WriteLineAsync($"  /password-reset#t={link.Token}");
            await output.WriteLineAsync($"Then enter this code: {link.Code}");
            await output.WriteLineAsync(
                $"The link works until {link.ExpiresAt:yyyy-MM-dd HH:mm} UTC; {link.CodeAttempts} wrong codes end it. It is shown only here, never in a log.");
            return 0;
        }
        catch (SqlException ex)
        {
            await error.WriteLineAsync($"{Name}: SQL Server error {ex.Number}. Nothing was changed.");
            return 1;
        }
    }

    private sealed class Account
    {
        public Guid Id { get; set; }

        public string UserName { get; set; } = "";

        public string DisplayName { get; set; } = "";

        public bool IsDisabled { get; set; }

        public bool IsPending { get; set; }

        public bool IsAdmin { get; set; }
    }
}

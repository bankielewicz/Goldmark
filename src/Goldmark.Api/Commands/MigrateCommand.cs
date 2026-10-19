using DbUp;
using DbUp.Engine.Output;
using Goldmark.Api.Configuration;
using Goldmark.Api.Data;
using Microsoft.Data.SqlClient;

namespace Goldmark.Api.Commands;

/// <summary>
/// <c>migrate</c>: applies the embedded forward-only migrations (DbUp) as the migration login,
/// then makes the managed runtime user a member of <c>goldmark_runtime</c> and verifies, as the
/// runtime login, that it holds nothing beyond that role (DEC-022, DEC-025). Run by the one-shot
/// <c>db-init</c> container after <c>bootstrap-db</c>; the application never migrates.
/// <para>
/// Refuses (exit 3, nothing changed) when the database already has a migration this build does
/// not contain, i.e. it was migrated by a newer or different build. Concurrent runs are
/// serialised with an application lock. Exit codes: 0 ok, 1 SQL/connection error or failed
/// script, 2 invalid configuration, 3 refused.
/// </para>
/// </summary>
internal static class MigrateCommand
{
    public const string Name = "migrate";
    private const int Refused = 3;

    // Serialises concurrent migrate runs. Session-owned: released when the connection closes.
    private const string LockSql = """
        DECLARE @result int;
        EXEC @result = sys.sp_getapplock @Resource = N'goldmark:migrate', @LockMode = N'Exclusive',
            @LockOwner = N'Session', @LockTimeout = 120000;
        SELECT @result;
        """;

    // Run as the migration login (db_owner). Adds the managed runtime user to goldmark_runtime.
    // Only a user that bootstrap-db created (or adopted) for the runtime login is touched.
    private const string EnsureRuntimeMemberSql = """
        SET NOCOUNT ON;
        DECLARE @uid int = (SELECT principal_id FROM sys.database_principals WHERE name = @user AND type = N'S');
        IF @uid IS NULL
            SELECT N'missing';
        ELSE IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 4 AND major_id = @uid
                AND name = N'goldmark:managed' AND CAST(value AS nvarchar(64)) = N'runtime-login')
            SELECT N'not-managed';
        ELSE IF IS_ROLEMEMBER(N'goldmark_runtime', @user) = 1
            SELECT N'member';
        ELSE
        BEGIN
            DECLARE @sql nvarchar(max) = N'ALTER ROLE goldmark_runtime ADD MEMBER ' + QUOTENAME(@user) + N';';
            EXEC sys.sp_executesql @sql;
            SELECT N'added';
        END;
        """;

    // Executed as the runtime login after migration. Everything bootstrap-db checks, plus the
    // schema- and object-level grants of goldmark_runtime (0002), in both directions: nothing
    // beyond DML on schema cmdb, no UPDATE/DELETE on the audit log, nothing on schema dbo beyond
    // reading the journal; and every one of those grants present, so a revoked grant fails here
    // rather than in the application. Rights on other objects and securables are checked by the
    // principal audit (PrincipalAudit.SelfAsync, #4). No rows = exactly as expected.
    // Keep in step with the migrations that grant to goldmark_runtime: a new grant (for example
    // EXECUTE on a schema) must be allowed here in the same change, or migrate refuses.
    internal const string VerifyRuntimeSql = """
        SET NOCOUNT ON;
        SELECT CAST(N'server role' AS nvarchar(40)) AS Kind, r.name COLLATE DATABASE_DEFAULT AS Name
        FROM sys.login_token AS t JOIN sys.server_principals AS r ON r.sid = t.sid
        WHERE t.type = N'SERVER ROLE' AND r.name <> N'public'
        UNION ALL
        SELECT N'database role', name COLLATE DATABASE_DEFAULT
        FROM sys.user_token WHERE type = N'ROLE' AND name NOT IN (N'public', N'goldmark_runtime')
        UNION ALL
        SELECT N'server permission', permission_name COLLATE DATABASE_DEFAULT
        FROM sys.fn_my_permissions(NULL, N'SERVER')
        WHERE permission_name NOT IN (N'CONNECT SQL', N'VIEW ANY DATABASE')
        UNION ALL
        SELECT N'database permission', permission_name COLLATE DATABASE_DEFAULT
        FROM sys.fn_my_permissions(NULL, N'DATABASE')
        WHERE permission_name NOT IN (N'CONNECT', N'VIEW ANY COLUMN ENCRYPTION KEY DEFINITION',
            N'VIEW ANY COLUMN MASTER KEY DEFINITION')
        UNION ALL
        SELECT N'permission on schema cmdb', permission_name COLLATE DATABASE_DEFAULT
        FROM sys.fn_my_permissions(N'cmdb', N'SCHEMA')
        WHERE permission_name NOT IN (N'SELECT', N'INSERT', N'UPDATE', N'DELETE')
        UNION ALL
        SELECT N'permission on schema dbo', permission_name COLLATE DATABASE_DEFAULT
        FROM sys.fn_my_permissions(N'dbo', N'SCHEMA')
        UNION ALL
        SELECT N'permission on dbo.SchemaVersions', permission_name COLLATE DATABASE_DEFAULT
        FROM sys.fn_my_permissions(N'dbo.SchemaVersions', N'OBJECT')
        WHERE permission_name <> N'SELECT'
        UNION ALL
        SELECT N'permission on cmdb.AuditLog', p.name
        FROM (VALUES (N'UPDATE'), (N'DELETE')) AS p(name)
        WHERE HAS_PERMS_BY_NAME(N'cmdb.AuditLog', N'OBJECT', p.name) = 1
        UNION ALL
        -- A column-level GRANT overrides the table-level DENY, so check every column too.
        SELECT N'permission on cmdb.AuditLog column', c.name COLLATE DATABASE_DEFAULT + N' UPDATE'
        FROM sys.columns AS c
        WHERE c.object_id = OBJECT_ID(N'cmdb.AuditLog')
            AND HAS_PERMS_BY_NAME(N'cmdb.AuditLog', N'OBJECT', N'UPDATE', c.name, N'COLUMN') = 1
        UNION ALL
        SELECT N'missing database role', N'goldmark_runtime' WHERE ISNULL(IS_ROLEMEMBER(N'goldmark_runtime'), 0) <> 1
        UNION ALL
        SELECT N'missing permission on schema cmdb', p.name
        FROM (VALUES (N'SELECT'), (N'INSERT'), (N'UPDATE'), (N'DELETE')) AS p(name)
        WHERE ISNULL(HAS_PERMS_BY_NAME(N'cmdb', N'SCHEMA', p.name), 0) = 0
        UNION ALL
        SELECT N'missing permission on cmdb.AuditLog', p.name
        FROM (VALUES (N'SELECT'), (N'INSERT')) AS p(name)
        WHERE ISNULL(HAS_PERMS_BY_NAME(N'cmdb.AuditLog', N'OBJECT', p.name), 0) = 0
        UNION ALL
        SELECT N'missing permission on dbo.SchemaVersions', N'SELECT'
        WHERE ISNULL(HAS_PERMS_BY_NAME(N'dbo.SchemaVersions', N'OBJECT', N'SELECT'), 0) = 0;
        """;

    // Executed as the migration login, before anything is read or changed: it must be the user
    // bootstrap-db created (or adopted) for this purpose. Its roles are checked with
    // BootstrapDatabaseCommand.VerifyMigrationSql.
    private const string MigrationMarkerSql = """
        SET NOCOUNT ON;
        SELECT CAST(N'missing marker' AS nvarchar(40)), N'goldmark:managed = migration-login'
        WHERE NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 4 AND major_id = USER_ID()
            AND name = N'goldmark:managed' AND CAST(value AS nvarchar(64)) = N'migration-login');
        """;

    public static async Task<int> RunAsync(IConfiguration configuration, TextWriter output, TextWriter error)
    {
        var errors = ConfigurationPreflight.BindingErrors(configuration);
        var db = new DatabaseOptions();
        var migration = new MigrationOptions();
        var bootstrap = new BootstrapOptions();
        if (errors.Count == 0)
        {
            db = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? db;
            migration = configuration.GetSection(MigrationOptions.SectionName).Get<MigrationOptions>() ?? migration;
            bootstrap = configuration.GetSection(BootstrapOptions.SectionName).Get<BootstrapOptions>() ?? bootstrap;
            errors = Validate(db, migration, bootstrap);
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

        var migrator = SqlConnectionFactory.Build(db, migration.User, migration.Password);
        migrator.Pooling = false;
        try
        {
            await using var connection = await CommandSql.OpenWithRetryAsync(
                migrator.ConnectionString, TimeSpan.FromSeconds(bootstrap.WaitSeconds), output, Name);

            // 0. Only the managed migration login, holding db_owner and nothing at server level,
            //    may migrate, also when migrate runs without bootstrap-db (Codex on #14).
            var principal = await CommandSql.FindingsAsync(connection, MigrationMarkerSql);
            principal.AddRange(await CommandSql.FindingsAsync(connection, BootstrapDatabaseCommand.VerifyMigrationSql));
            if (principal.Count > 0)
            {
                return await RefuseAsync(error,
                    $"the migration login '{migration.User}' must be the one bootstrap-db manages, db_owner of '{db.Name}' " +
                    $"and nothing else: {string.Join("; ", principal)}. Nothing was changed.");
            }

            // The principal audit's effective-rights layer, as the migration login (DEC-031). The
            // pass over other databases is bootstrap-db's (it opens every database).
            var policy = FindingPolicy.Parse(bootstrap.AcceptedFindings);
            if (policy.Refused(await PrincipalAudit.SelfAsync(connection, db.Name, runtime: false, databases: false), output, Name) is { Count: > 0 } migrationFindings)
            {
                return await RefuseAsync(error, $"{FindingPolicy.Describe(migrationFindings)}{Environment.NewLine}Nothing was changed.");
            }

            if (await ScalarAsync(connection, LockSql) < 0)
            {
                return await RefuseAsync(error, "another migrate run holds the migration lock (waited 120 s).");
            }

            // 1. The database must not be ahead of, or diverge from, this build.
            var applied = await AppliedScriptsAsync(connection);
            var unknown = applied.Where(s => !SchemaMigrations.Scripts.Contains(s, StringComparer.Ordinal)).ToList();
            if (unknown.Count > 0)
            {
                return await RefuseAsync(error,
                    $"the database has migrations this build does not contain ({string.Join(", ", unknown)}): it was migrated by a " +
                    $"newer or different Goldmark build (database schema version {SchemaMigrations.DatabaseVersion(applied)}, " +
                    $"this build: {SchemaMigrations.ExpectedVersion}). Nothing was changed.");
            }

            // 2. Apply what is missing, each script in its own transaction.
            var upgrade = DeployChanges.To
                .SqlDatabase(() => new SqlConnection(migrator.ConnectionString))
                .WithScriptsEmbeddedInAssembly(typeof(MigrateCommand).Assembly, SchemaMigrations.IsMigrationResource)
                .JournalToSqlTable(SchemaMigrations.JournalSchema, SchemaMigrations.JournalTable)
                .WithTransactionPerScript()
                .WithVariablesDisabled()
                .WithExecutionTimeout(TimeSpan.FromMinutes(10))
                .LogTo(new UpgradeLog(output, error))
                .Build()
                .PerformUpgrade();
            if (!upgrade.Successful)
            {
                var number = (upgrade.Error as SqlException)?.Number;
                await error.WriteLineAsync(
                    $"{Name}: migration {upgrade.ErrorScript?.Name ?? "(none)"} failed{(number is null ? "" : $" (SQL Server error {number})")}: " +
                    $"{upgrade.Error?.Message}. It was rolled back; earlier scripts stay applied.");
                return 1;
            }

            await output.WriteLineAsync(upgrade.Scripts.Any()
                ? $"{Name}: applied {upgrade.Scripts.Count()} migration(s)"
                : $"{Name}: no migrations to apply");

            // 3. The runtime login gets its rights only through goldmark_runtime.
            switch (await ScalarTextAsync(connection, EnsureRuntimeMemberSql, db.User))
            {
                case "missing":
                    return await RefuseAsync(error, $"the runtime database user '{db.User}' does not exist. Run bootstrap-db first.");
                case "not-managed":
                    return await RefuseAsync(error, $"the runtime database user '{db.User}' was not created by Goldmark. Run bootstrap-db first.");
                case "added":
                    await output.WriteLineAsync($"{Name}: added runtime user '{db.User}' to {BootstrapDatabaseCommand.RuntimeRole}");
                    break;
            }

            var runtime = SqlConnectionFactory.Build(db);
            runtime.Pooling = false;
            await using (var runtimeConnection = new SqlConnection(runtime.ConnectionString))
            {
                await runtimeConnection.OpenAsync();
                if (await CommandSql.FindingsAsync(runtimeConnection, VerifyRuntimeSql) is { Count: > 0 } findings)
                {
                    return await RefuseAsync(error,
                        $"the runtime login '{db.User}' must hold exactly the rights of {BootstrapDatabaseCommand.RuntimeRole}: " +
                        $"{string.Join("; ", findings)}. Restore the role's grants and remove any others.");
                }

                if (policy.Refused(await PrincipalAudit.SelfAsync(runtimeConnection, db.Name, runtime: true, databases: false), output, Name) is { Count: > 0 } runtimeFindings)
                {
                    return await RefuseAsync(error, FindingPolicy.Describe(runtimeFindings));
                }
            }


            await output.WriteLineAsync($"{Name}: verified runtime login '{db.User}' holds only {BootstrapDatabaseCommand.RuntimeRole}.");
            await output.WriteLineAsync($"{Name}: database schema version {SchemaMigrations.DatabaseVersion(await AppliedScriptsAsync(connection))} " +
                $"(this build expects {SchemaMigrations.ExpectedVersion})");
            return 0;
        }
        catch (SqlException ex)
        {
            await error.WriteLineAsync($"{Name}: SQL Server error {ex.Number}: {ex.Message}");
            return 1;
        }
        catch (OperationCanceledException)
        {
            await error.WriteLineAsync($"{Name}: SQL Server did not accept connections within {bootstrap.WaitSeconds} s.");
            return 1;
        }
    }

    internal static List<string> Validate(DatabaseOptions db, MigrationOptions migration, BootstrapOptions bootstrap)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(db.Host))
        {
            errors.Add($"{DatabaseOptions.SectionName}:Host is required.");
        }

        if (db.Port is < 1 or > 65535)
        {
            errors.Add($"{DatabaseOptions.SectionName}:Port must be between 1 and 65535.");
        }

        if (!SqlIdentifier.IsValid(db.Name) || SqlIdentifier.IsSystemDatabase(db.Name))
        {
            errors.Add($"{DatabaseOptions.SectionName}:Name must be a simple identifier and not a system database.");
        }

        if (!SqlIdentifier.IsValid(db.User) || SqlIdentifier.IsReservedPrincipal(db.User))
        {
            errors.Add($"{DatabaseOptions.SectionName}:User must be a simple identifier and not a reserved name.");
        }

        if (!SqlIdentifier.IsValid(migration.User) || SqlIdentifier.IsReservedPrincipal(migration.User)
            || string.Equals(migration.User, db.User, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{MigrationOptions.SectionName}:User must be a simple identifier, not a reserved name, and differ from {DatabaseOptions.SectionName}:User.");
        }

        if (string.IsNullOrEmpty(db.Password))
        {
            errors.Add($"{DatabaseOptions.SectionName}:Password is required (the runtime login is verified after migrating).");
        }

        if (string.IsNullOrEmpty(migration.Password))
        {
            errors.Add($"{MigrationOptions.SectionName}:Password is required.");
        }
        else
        {
            // Same separation as bootstrap-db, also when migrate runs on its own: the app's
            // credential must never authenticate as db_owner, nor the migrator as the admin.
            if (string.Equals(migration.Password, db.Password, StringComparison.Ordinal))
            {
                errors.Add($"{MigrationOptions.SectionName}:Password must differ from {DatabaseOptions.SectionName}:Password.");
            }

            if (string.Equals(migration.Password, bootstrap.AdminPassword, StringComparison.Ordinal))
            {
                errors.Add($"{MigrationOptions.SectionName}:Password must differ from {BootstrapOptions.SectionName}:AdminPassword.");
            }
        }

        if (!string.IsNullOrEmpty(db.Password) && string.Equals(db.Password, bootstrap.AdminPassword, StringComparison.Ordinal))
        {
            errors.Add($"{DatabaseOptions.SectionName}:Password must differ from {BootstrapOptions.SectionName}:AdminPassword.");
        }

        errors.AddRange(BootstrapDatabaseCommand.CommonConnectionErrors(db));
        errors.AddRange(FindingPolicy.Errors(bootstrap.AcceptedFindings));
        if (bootstrap.WaitSeconds is < 0 or > 900)
        {
            errors.Add($"{BootstrapOptions.SectionName}:WaitSeconds must be between 0 and 900.");
        }

        return errors;
    }

    private static async Task<int> RefuseAsync(TextWriter error, string reason)
    {
        await error.WriteLineAsync($"{Name}: refusing to continue: {reason}");
        return Refused;
    }

    private static async Task<int> ScalarAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 180;
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<string?> ScalarTextAsync(SqlConnection connection, string sql, string user)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 60;
        command.Parameters.Add(new SqlParameter("@user", System.Data.SqlDbType.NVarChar, 128) { Value = user });
        return await command.ExecuteScalarAsync() as string;
    }

    internal static async Task<List<string>> AppliedScriptsAsync(SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = SchemaMigrations.AppliedScriptsSql;
        command.CommandTimeout = 30;
        await using var reader = await command.ExecuteReaderAsync();
        var scripts = new List<string>();
        while (await reader.ReadAsync())
        {
            scripts.Add(reader.GetString(0));
        }

        return scripts;
    }

    /// <summary>DbUp's progress messages, prefixed like the other command output.</summary>
    private sealed class UpgradeLog(TextWriter output, TextWriter error) : IUpgradeLog
    {
        public void LogTrace(string format, params object[] args) { }

        public void LogDebug(string format, params object[] args) { }

        public void LogInformation(string format, params object[] args) => Write(output, format, args);

        public void LogWarning(string format, params object[] args) => Write(error, format, args);

        public void LogError(string format, params object[] args) => Write(error, format, args);

        public void LogError(Exception ex, string format, params object[] args) => Write(error, format, args);

        private static void Write(TextWriter writer, string format, object[] args) =>
            writer.WriteLine($"{Name}: {string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args)}");
    }
}

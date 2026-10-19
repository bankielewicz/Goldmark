using Goldmark.Api.Configuration;
using Goldmark.Api.Data;
using Microsoft.Data.SqlClient;

namespace Goldmark.Api.Commands;

/// <summary>
/// <c>bootstrap-db</c>: one-shot, idempotent provisioning of the Goldmark database and its two
/// logins, run with administrative credentials (CONTROL SERVER) that only the bootstrap
/// container receives. It has no Docker dependency: an installer can run the same command.
/// <list type="bullet">
/// <item>The <b>runtime login</b> (<c>Goldmark:Database:User</c>) gets a database user and
/// nothing else here; <c>migrate</c> makes it a member of <c>goldmark_runtime</c>, the role the
/// migrations define.</item>
/// <item>The <b>migration login</b> (<c>Goldmark:Migration:User</c>) gets a database user that is
/// a member of <c>db_owner</c> of this database only (DEC-024).</item>
/// </list>
/// <para>
/// Goldmark only manages principals it created itself, recognised by a marker on the database
/// user whose value names the principal's purpose. A login or database user that already exists
/// without that marker is refused (exit 3) with nothing changed, unless the operator opts in to
/// adopting it with <see cref="BootstrapOptions.AdoptExistingLogin"/>. In
/// <see cref="BootstrapOptions.ExistingMode"/> nothing is created at all: a DBA has created the
/// database, both logins and their users, and only the marker is added (DEC-032).
/// </para>
/// <para>
/// Every run audits both principals and the instance (<see cref="PrincipalAudit"/>, DEC-031):
/// from the administrator's side before anything is changed and again afterwards, then as each
/// login. Both logins, both users, the markers and the <c>db_owner</c> membership are created in
/// one transaction.
/// </para>
/// </summary>
internal static class BootstrapDatabaseCommand
{
    public const string Name = "bootstrap-db";
    public const string ManagedMarker = "goldmark:managed";
    public const string RuntimeMarkerValue = "runtime-login";
    public const string MigrationMarkerValue = "migration-login";
    public const string RuntimeRole = "goldmark_runtime";
    private const int MaxPasswordLength = 128; // QUOTENAME input limit; longer input would yield NULL.
    private const int Refused = 3;

    // Read-only. Run in master. Login existence, type and whether the password already matches.
    private const string LoginStateSql = """
        SET NOCOUNT ON;
        SELECT CAST(p.type AS nvarchar(2)), CAST(ISNULL(PWDCOMPARE(@password, l.password_hash), 0) AS int)
        FROM sys.server_principals AS p LEFT JOIN sys.sql_logins AS l ON l.principal_id = p.principal_id
        WHERE p.name = @login;
        """;

    private const string DatabaseExistsSql = "SELECT CASE WHEN DB_ID(@db) IS NULL THEN 0 ELSE 1 END;";

    // Read-only. Run in the Goldmark database: user mapped to the login (by SID), the value of
    // Goldmark's marker on that user (NULL if none), whether a user of that name belongs to
    // someone else, the mapped user's name (#15) and whether it is a member of db_owner.
    private const string UserStateSql = """
        SET NOCOUNT ON;
        DECLARE @sid varbinary(85) = SUSER_SID(@login);
        DECLARE @uid int = (SELECT principal_id FROM sys.database_principals WHERE sid = @sid);
        SELECT
            CASE WHEN @uid IS NULL THEN 0 ELSE 1 END,
            (SELECT CAST(value AS nvarchar(64)) FROM sys.extended_properties
                WHERE class = 4 AND major_id = @uid AND name = N'goldmark:managed'),
            CASE WHEN EXISTS (SELECT 1 FROM sys.database_principals
                WHERE name = @login AND (@sid IS NULL OR sid <> @sid)) THEN 1 ELSE 0 END,
            USER_NAME(@uid),
            CASE WHEN @uid IS NOT NULL AND IS_ROLEMEMBER(N'db_owner', USER_NAME(@uid)) = 1 THEN 1 ELSE 0 END;
        """;

    // Read-only. Run in master: whether the administrator can see and change everything the
    // audit and provisioning need (PWDCOMPARE reads password hashes, which needs CONTROL SERVER).
    private const string AdminRightsSql = "SELECT ISNULL(HAS_PERMS_BY_NAME(NULL, NULL, 'CONTROL SERVER'), 0);";

    // Read-only. The migrations use SQL Server 2025's native json type (DEC-030: 2025 is the
    // minimum), so an older server is refused before anything is created (Codex on #17).
    private const string ServerVersionSql = "SELECT CAST(SERVERPROPERTY('ProductMajorVersion') AS int);";
    private const int MinimumMajorVersion = 17;

    // Identifiers and the password are parameters, quoted server-side with QUOTENAME,
    // because DDL statements cannot take parameters directly.
    // AUTO_CLOSE (on by default for new databases on Express) would close the database whenever
    // its last connection ends, so each readiness probe and each audit pass would reopen it.
    private const string CreateDatabaseSql = """
        DECLARE @sql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@db) + N';';
        EXEC sys.sp_executesql @sql;
        SET @sql = N'ALTER DATABASE ' + QUOTENAME(@db) + N' SET AUTO_CLOSE OFF;';
        EXEC sys.sp_executesql @sql;
        """;

    // Validates the password against SQL Server's policy without keeping anything, so a
    // rejected password cannot leave a new, empty database behind.
    private const string PasswordPolicyProbeSql = """
        SET XACT_ABORT ON;
        BEGIN TRANSACTION;
        DECLARE @sql nvarchar(max) = CASE WHEN SUSER_ID(@login) IS NULL
            THEN N'CREATE LOGIN ' + QUOTENAME(@login) + N' WITH PASSWORD = ' + QUOTENAME(@password, N'''') + N', CHECK_POLICY = ON;'
            ELSE N'ALTER LOGIN ' + QUOTENAME(@login) + N' WITH PASSWORD = ' + QUOTENAME(@password, N'''') + N';' END;
        EXEC sys.sp_executesql @sql;
        ROLLBACK TRANSACTION;
        """;

    // Run in the Goldmark database, inside the caller's transaction, once per principal.
    // @adopt_user remaps an existing same-name user (adoption only). @db_owner adds the user to
    // db_owner (migration login only). @create = 0 (Existing mode) only adds the marker: anything
    // else would be a bug in the checks before it, so it fails instead of changing a principal.
    private const string ProvisionSql = """
        SET NOCOUNT ON;
        SET XACT_ABORT ON;
        DECLARE @sql nvarchar(max);
        IF @create = 0 AND (SUSER_ID(@login) IS NULL
            OR ISNULL((SELECT PWDCOMPARE(@password, password_hash) FROM sys.sql_logins WHERE name = @login), 0) = 0
            OR NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE sid = SUSER_SID(@login))
            OR (@db_owner = 1 AND IS_ROLEMEMBER(N'db_owner', (SELECT name FROM sys.database_principals WHERE sid = SUSER_SID(@login))) = 0))
            THROW 50001, N'bootstrap-db (Mode Existing) would have to create or change a login or user; nothing was changed.', 1;
        IF SUSER_ID(@login) IS NULL
        BEGIN
            SET @sql = N'CREATE LOGIN ' + QUOTENAME(@login) + N' WITH PASSWORD = ' + QUOTENAME(@password, N'''')
                + N', DEFAULT_DATABASE = ' + QUOTENAME(@db) + N', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;';
            EXEC sys.sp_executesql @sql;
            SELECT N'created login';
        END
        ELSE IF ISNULL((SELECT PWDCOMPARE(@password, password_hash) FROM sys.sql_logins WHERE name = @login), 0) = 0
        BEGIN
            SET @sql = N'ALTER LOGIN ' + QUOTENAME(@login) + N' WITH PASSWORD = ' + QUOTENAME(@password, N'''') + N';';
            EXEC sys.sp_executesql @sql;
            SELECT N'changed login password';
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE sid = SUSER_SID(@login))
        BEGIN
            IF @adopt_user = 1 AND USER_ID(@login) IS NOT NULL
            BEGIN
                SET @sql = N'ALTER USER ' + QUOTENAME(@login) + N' WITH LOGIN = ' + QUOTENAME(@login) + N';';
                EXEC sys.sp_executesql @sql;
                SELECT N'remapped existing database user';
            END
            ELSE
            BEGIN
                SET @sql = N'CREATE USER ' + QUOTENAME(@login) + N' FOR LOGIN ' + QUOTENAME(@login) + N';';
                EXEC sys.sp_executesql @sql;
                SELECT N'created database user';
            END;
        END;

        DECLARE @user sysname = (SELECT name FROM sys.database_principals WHERE sid = SUSER_SID(@login));
        IF NOT EXISTS (SELECT 1 FROM sys.extended_properties
            WHERE class = 4 AND major_id = USER_ID(@user) AND name = N'goldmark:managed')
        BEGIN
            EXEC sys.sp_addextendedproperty @name = N'goldmark:managed', @value = @marker,
                @level0type = N'USER', @level0name = @user;
            SELECT N'marked database user as managed by Goldmark';
        END;

        IF @db_owner = 1 AND IS_ROLEMEMBER(N'db_owner', @user) = 0
        BEGIN
            SET @sql = N'ALTER ROLE db_owner ADD MEMBER ' + QUOTENAME(@user) + N';';
            EXEC sys.sp_executesql @sql;
            SELECT N'added database user to db_owner';
        END;
        """;

    // Executed as the runtime login. Returns one row per role membership or effective
    // permission beyond the baseline that a fresh CONNECT-only login has on SQL Server 2025
    // (measured: server CONNECT SQL + VIEW ANY DATABASE; database CONNECT + the two VIEW ANY
    // COLUMN ... KEY DEFINITION grants that public holds), plus membership of goldmark_runtime,
    // whose grants are schema- and object-level (checked by migrate). No rows = least privilege.
    // System views differ in collation, so every column is collated to the database default.
    internal const string VerifySql = """
        SET NOCOUNT ON;
        SELECT CAST(N'server role' AS nvarchar(32)) AS Kind, r.name COLLATE DATABASE_DEFAULT AS Name
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
            N'VIEW ANY COLUMN MASTER KEY DEFINITION');
        """;

    // Executed as the migration login: db_owner of this database, and nothing at server level
    // beyond the CONNECT-only baseline.
    internal const string VerifyMigrationSql = """
        SET NOCOUNT ON;
        SELECT CAST(N'server role' AS nvarchar(32)) AS Kind, r.name COLLATE DATABASE_DEFAULT AS Name
        FROM sys.login_token AS t JOIN sys.server_principals AS r ON r.sid = t.sid
        WHERE t.type = N'SERVER ROLE' AND r.name <> N'public'
        UNION ALL
        SELECT N'database role', name COLLATE DATABASE_DEFAULT
        FROM sys.user_token WHERE type = N'ROLE' AND name NOT IN (N'public', N'db_owner')
        UNION ALL
        SELECT N'server permission', permission_name COLLATE DATABASE_DEFAULT
        FROM sys.fn_my_permissions(NULL, N'SERVER')
        WHERE permission_name NOT IN (N'CONNECT SQL', N'VIEW ANY DATABASE')
        UNION ALL
        SELECT N'missing database role', N'db_owner' WHERE ISNULL(IS_ROLEMEMBER(N'db_owner'), 0) <> 1;
        """;

    /// <summary>A login Goldmark manages, with the settings and marker that identify it.</summary>
    private sealed record Principal(
        string Purpose, string Login, string Password, string MarkerValue, bool DbOwner, string ExpectedRole,
        string UserVariable, string PasswordVariable);

    private readonly record struct LoginState(string Type, bool PasswordMatches);

    private readonly record struct UserState(bool Mapped, string? Marker, bool NameTakenByOther, string? Name, bool DbOwner);

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

        var existing = bootstrap.Mode == BootstrapOptions.ExistingMode;
        Principal[] principals =
        [
            new("runtime", db.User, db.Password, RuntimeMarkerValue, DbOwner: false, RuntimeRole, "GOLDMARK_DB_USER", "GOLDMARK_DB_PASSWORD"),
            new("migration", migration.User, migration.Password, MigrationMarkerValue, DbOwner: true, "db_owner",
                "GOLDMARK_DB_MIGRATION_USER", "GOLDMARK_DB_MIGRATION_PASSWORD"),
        ];
        var policy = FindingPolicy.Parse(bootstrap.AcceptedFindings);

        var admin = SqlConnectionFactory.Build(db, bootstrap.AdminUser, bootstrap.AdminPassword, database: "master");
        admin.Pooling = false;
        try
        {
            await using var connection = await CommandSql.OpenWithRetryAsync(
                admin.ConnectionString, TimeSpan.FromSeconds(bootstrap.WaitSeconds), output, Name);

            // 1. Read-only: decide everything, for both logins, before changing anything.
            if (await ScalarAsync(connection, AdminRightsSql, db) != 1)
            {
                return await RefuseAsync(error,
                    $"the administrator login '{bootstrap.AdminUser}' needs CONTROL SERVER (for example sysadmin) to audit and provision " +
                    "Goldmark's logins. Nothing was changed.");
            }

            if (await ScalarAsync(connection, ServerVersionSql, db) is var major and < MinimumMajorVersion)
            {
                return await RefuseAsync(error,
                    $"Goldmark needs SQL Server 2025 (major version {MinimumMajorVersion}) or later; this server is major version {major}. Nothing was changed.");
            }

            var databaseExists = await ScalarAsync(connection, DatabaseExistsSql, db) == 1;
            if (existing && !databaseExists)
            {
                return await RefuseAsync(error,
                    $"Mode {BootstrapOptions.ExistingMode}: the database '{db.Name}' does not exist, and this mode creates nothing. " +
                    $"Create it first (CREATE DATABASE [{db.Name}];), or use Mode {BootstrapOptions.CreateMode}. Nothing was changed.");
            }

            var states = new List<(Principal Principal, LoginState? Login, UserState User)>();
            foreach (var principal in principals)
            {
                var login = await LoginStateAsync(connection, db, principal);
                var user = new UserState(false, null, false, null, false);
                if (databaseExists)
                {
                    await connection.ChangeDatabaseAsync(db.Name);
                    user = await UserStateAsync(connection, db, principal);
                    await connection.ChangeDatabaseAsync("master");
                }

                if (Refusal(principal, login, user, bootstrap, db.Name) is { } reason)
                {
                    return await RefuseAsync(error, reason);
                }

                if (existing)
                {
                    Report(output, principal, $"using the existing login '{principal.Login}' (Mode {BootstrapOptions.ExistingMode}: nothing is created)");
                }
                else if (bootstrap.AdoptExistingLogin && !IsManaged(principal, login, user) && (login is not null || user.NameTakenByOther))
                {
                    Report(output, principal, $"adopting the existing {(login is not null ? "login" : "database user")} '{principal.Login}' (GOLDMARK_DB_ADOPT_EXISTING_LOGIN=true)");
                }

                states.Add((principal, login, user));
            }

            // The principal audit, from the administrator's side, before any change (DEC-031).
            if (policy.Refused(await AdminAuditAsync(connection, db, principals, bootstrap, otherDatabases: true), output, Name) is { Count: > 0 } before)
            {
                return await RefuseAsync(error, $"{FindingPolicy.Describe(before)}{Environment.NewLine}Nothing was changed.");
            }

            // 2. Provision. Passwords are checked against SQL Server's policy before the database
            //    is created; logins, users, markers and db_owner membership then change together.
            //    In Existing mode the only change is the marker.
            if (!existing)
            {
                foreach (var (principal, login, _) in states)
                {
                    if (login is not { PasswordMatches: true })
                    {
                        await ExecuteAsync(connection, PasswordPolicyProbeSql, db, principal, bootstrap);
                    }
                }

                if (!databaseExists)
                {
                    await ExecuteAsync(connection, CreateDatabaseSql, db, principals[0], bootstrap);
                    await output.WriteLineAsync($"{Name}: created database");
                }
            }

            await connection.ChangeDatabaseAsync(db.Name);
            await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync())
            {
                foreach (var principal in principals)
                {
                    foreach (var step in await StepsAsync(connection, transaction, db, principal, bootstrap))
                    {
                        Report(output, principal, step);
                    }
                }

                await transaction.CommitAsync();
            }

            await connection.ChangeDatabaseAsync("master");

            // 3. Audit again: principals created by this run had nothing to check before.
            if (policy.Refused(await AdminAuditAsync(connection, db, principals, bootstrap, otherDatabases: false), output, Name) is { Count: > 0 } after)
            {
                return await RefuseAsync(error, $"{FindingPolicy.Describe(after)}{Environment.NewLine}The changes listed above were made; fix these and run db-init again.");
            }

            // 4. Verify each login's effective permissions, as that login.
            var (runtimeExcess, runtimeFindings) = await VerifyAsync(db, principals[0], VerifySql, runtime: true);
            if (runtimeExcess.Count > 0)
            {
                return await RefuseAsync(error,
                    $"the runtime login '{db.User}' has rights beyond CONNECT and {RuntimeRole}: {string.Join("; ", runtimeExcess)}. Remove them.");
            }

            var (migrationExcess, migrationFindings) = await VerifyAsync(db, principals[1], VerifyMigrationSql, runtime: false);
            if (migrationExcess.Count > 0)
            {
                return await RefuseAsync(error,
                    $"the migration login '{migration.User}' must be db_owner of '{db.Name}' and hold nothing else: {string.Join("; ", migrationExcess)}.");
            }

            if (policy.Refused([.. runtimeFindings, .. migrationFindings], output, Name) is { Count: > 0 } effective)
            {
                return await RefuseAsync(error, FindingPolicy.Describe(effective));
            }

            await output.WriteLineAsync($"{Name}: verified runtime login '{db.User}' has no role memberships or permissions beyond CONNECT and {RuntimeRole}.");
            await output.WriteLineAsync($"{Name}: verified migration login '{migration.User}' is db_owner of '{db.Name}' with no server roles or permissions beyond CONNECT.");
            policy.ReportUnmatched(output, Name);
            await output.WriteLineAsync($"{Name}: principal audit passed for both logins and the instance.");
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

    private static async Task<List<AuditFinding>> AdminAuditAsync(
        SqlConnection connection, DatabaseOptions db, Principal[] principals, BootstrapOptions bootstrap, bool otherDatabases)
    {
        var findings = await PrincipalAudit.EnvironmentAsync(connection, db.Name);
        if (otherDatabases)
        {
            findings.AddRange(await PrincipalAudit.DatabasesAsync(connection, db.Name, principals[0].Login, principals[1].Login));
        }

        findings.AddRange(await PrincipalAudit.GoldmarkDatabaseAsync(connection, db.Name, principals[0].Login, principals[1].Login));
        foreach (var principal in principals)
        {
            findings.AddRange(await PrincipalAudit.PrincipalAsync(connection, db.Name, principal.Login, principal.ExpectedRole, bootstrap.AdoptExistingLogin));
        }

        return findings;
    }

    private static bool IsManaged(Principal principal, LoginState? login, UserState user) =>
        login is not null && user.Mapped && user.Marker == principal.MarkerValue;

    /// <summary>Why this principal must not be touched, or null if it may be provisioned.</summary>
    private static string? Refusal(Principal principal, LoginState? login, UserState user, BootstrapOptions bootstrap, string database)
    {
        if (login is { Type: not "S" } nonSql)
        {
            return $"'{principal.Login}' exists but is not a SQL Server login (type {nonSql.Type}).";
        }

        if (user.Mapped && user.Name == "dbo")
        {
            return $"the {principal.Purpose} login '{principal.Login}' owns the database '{database}' (it is its dbo). Nothing was changed. " +
                $"Fix: ALTER AUTHORIZATION ON DATABASE::{AuditFinding.Q(database)} TO <a login with CONTROL SERVER>;";
        }

        if (user.Mapped && !string.Equals(user.Name, principal.Login, StringComparison.Ordinal))
        {
            // migrate finds the user by name, so a differently named user would fail there (#15).
            return $"the database user mapped to the {principal.Purpose} login '{principal.Login}' in '{database}' is named '{user.Name}'; " +
                $"Goldmark requires it to have the login's name. Nothing was changed. " +
                $"Fix: USE {AuditFinding.Q(database)}; ALTER USER {AuditFinding.Q(user.Name!)} WITH NAME = {AuditFinding.Q(principal.Login)};";
        }

        if (user.Mapped && user.Marker is not null && user.Marker != principal.MarkerValue)
        {
            // Never let one managed principal be reused for the other purpose (e.g. swapped names).
            return $"'{principal.Login}' is managed by Goldmark as the {user.Marker} and cannot be used as the {principal.Purpose} login. Nothing was changed.";
        }

        if (bootstrap.Mode == BootstrapOptions.ExistingMode)
        {
            return ExistingModeRefusal(principal, login, user, database);
        }

        if (!IsManaged(principal, login, user) && !bootstrap.AdoptExistingLogin && (login is not null || user.NameTakenByOther))
        {
            var what = login is not null ? $"login '{principal.Login}'" : $"database user '{principal.Login}'";
            return $"the {what} already exists and was not created by Goldmark. Nothing was changed. " +
                $"Configure a different {principal.UserVariable}, or set GOLDMARK_DB_ADOPT_EXISTING_LOGIN=true to take it over " +
                $"(its password is then set to {principal.PasswordVariable}).";
        }

        return null;
    }

    /// <summary>Existing mode creates and changes nothing: everything must already be in place.</summary>
    private static string? ExistingModeRefusal(Principal principal, LoginState? login, UserState user, string database)
    {
        var mode = $"Mode {BootstrapOptions.ExistingMode}";
        if (login is null)
        {
            return $"{mode}: the {principal.Purpose} login '{principal.Login}' does not exist, and this mode creates nothing. Nothing was changed. " +
                $"Fix: CREATE LOGIN [{principal.Login}] WITH PASSWORD = '...', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF; " +
                $"with the password in {principal.PasswordVariable}. See docs/ops/external-sql-server.md.";
        }

        if (!login.Value.PasswordMatches)
        {
            return $"{mode}: the password of the {principal.Purpose} login '{principal.Login}' differs from {principal.PasswordVariable}, " +
                "and this mode never changes passwords. Nothing was changed.";
        }

        if (user.NameTakenByOther)
        {
            return $"{mode}: the database user '{principal.Login}' in '{database}' belongs to a different login. Nothing was changed.";
        }

        if (!user.Mapped)
        {
            return $"{mode}: the {principal.Purpose} login '{principal.Login}' has no user in '{database}'. Nothing was changed. " +
                $"Fix: USE [{database}]; CREATE USER [{principal.Login}] FOR LOGIN [{principal.Login}];";
        }

        if (principal.DbOwner && !user.DbOwner)
        {
            return $"{mode}: the migration login's user '{principal.Login}' is not a member of db_owner in '{database}'. Nothing was changed. " +
                $"Fix: USE [{database}]; ALTER ROLE db_owner ADD MEMBER [{principal.Login}];";
        }

        return null;
    }

    private static void Report(TextWriter output, Principal principal, string message) =>
        output.WriteLine($"{Name}: {principal.Purpose} login: {message}");

    private static async Task<int> RefuseAsync(TextWriter error, string reason)
    {
        await error.WriteLineAsync($"{Name}: refusing to continue: {reason}");
        return Refused;
    }

    private static SqlCommand Command(SqlConnection connection, string sql, DatabaseOptions db, Principal principal, BootstrapOptions? bootstrap = null)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 60;
        command.Parameters.Add(new SqlParameter("@db", System.Data.SqlDbType.NVarChar, 128) { Value = db.Name });
        command.Parameters.Add(new SqlParameter("@login", System.Data.SqlDbType.NVarChar, 128) { Value = principal.Login });
        command.Parameters.Add(new SqlParameter("@password", System.Data.SqlDbType.NVarChar, 128) { Value = principal.Password });
        command.Parameters.Add(new SqlParameter("@marker", System.Data.SqlDbType.NVarChar, 64) { Value = principal.MarkerValue });
        command.Parameters.Add(new SqlParameter("@db_owner", System.Data.SqlDbType.Bit) { Value = principal.DbOwner });
        command.Parameters.Add(new SqlParameter("@adopt_user", System.Data.SqlDbType.Bit) { Value = bootstrap?.AdoptExistingLogin ?? false });
        command.Parameters.Add(new SqlParameter("@create", System.Data.SqlDbType.Bit) { Value = bootstrap?.Mode != BootstrapOptions.ExistingMode });
        return command;
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql, DatabaseOptions db, Principal principal, BootstrapOptions bootstrap)
    {
        await using var command = Command(connection, sql, db, principal, bootstrap);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ScalarAsync(SqlConnection connection, string sql, DatabaseOptions db)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqlParameter("@db", System.Data.SqlDbType.NVarChar, 128) { Value = db.Name });
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<LoginState?> LoginStateAsync(SqlConnection connection, DatabaseOptions db, Principal principal)
    {
        await using var command = Command(connection, LoginStateSql, db, principal);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? new LoginState(reader.GetString(0).Trim(), reader.GetInt32(1) == 1) : null;
    }

    private static async Task<UserState> UserStateAsync(SqlConnection connection, DatabaseOptions db, Principal principal)
    {
        await using var command = Command(connection, UserStateSql, db, principal);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new UserState(reader.GetInt32(0) == 1, reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetInt32(2) == 1,
            reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetInt32(4) == 1);
    }

    private static async Task<List<string>> StepsAsync(
        SqlConnection connection, SqlTransaction transaction, DatabaseOptions db, Principal principal, BootstrapOptions bootstrap)
    {
        await using var command = Command(connection, ProvisionSql, db, principal, bootstrap);
        command.Transaction = transaction;
        await using var reader = await command.ExecuteReaderAsync();
        var steps = new List<string>();
        do
        {
            while (await reader.ReadAsync())
            {
                steps.Add(reader.GetString(0));
            }
        }
        while (await reader.NextResultAsync());
        return steps;
    }

    /// <summary>Runs, as the login itself, its role/permission check and the principal audit's effective-rights layer.</summary>
    private static async Task<(List<string> Excess, List<AuditFinding> Findings)> VerifyAsync(DatabaseOptions db, Principal principal, string sql, bool runtime)
    {
        var builder = SqlConnectionFactory.Build(db, principal.Login, principal.Password);
        builder.Pooling = false;
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        return (await CommandSql.FindingsAsync(connection, sql), await PrincipalAudit.SelfAsync(connection, db.Name, runtime));
    }

    internal static List<string> Validate(DatabaseOptions db, MigrationOptions migration, BootstrapOptions bootstrap)
    {
        var errors = new List<string>();
        const string database = DatabaseOptions.SectionName;
        const string migrations = MigrationOptions.SectionName;
        const string admin = BootstrapOptions.SectionName;
        if (string.IsNullOrWhiteSpace(db.Host))
        {
            errors.Add($"{database}:Host is required.");
        }

        if (db.Port is < 1 or > 65535)
        {
            errors.Add($"{database}:Port must be between 1 and 65535.");
        }

        if (!SqlIdentifier.IsValid(db.Name))
        {
            errors.Add($"{database}:Name must be a simple identifier.");
        }
        else if (SqlIdentifier.IsSystemDatabase(db.Name))
        {
            errors.Add($"{database}:Name must not be a SQL Server system database.");
        }

        LoginErrors(errors, $"{database}:User", db.User, bootstrap.AdminUser);
        LoginErrors(errors, $"{migrations}:User", migration.User, bootstrap.AdminUser);
        if (string.Equals(db.User, migration.User, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{migrations}:User must differ from {database}:User.");
        }

        if (db.Password.Length is < 12 or > MaxPasswordLength)
        {
            errors.Add($"{database}:Password must be 12 to {MaxPasswordLength} characters.");
        }

        if (migration.Password.Length is < 12 or > MaxPasswordLength)
        {
            errors.Add($"{migrations}:Password must be 12 to {MaxPasswordLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(bootstrap.AdminUser) || string.IsNullOrEmpty(bootstrap.AdminPassword))
        {
            errors.Add($"{admin}:AdminUser and AdminPassword are required.");
        }
        else
        {
            if (string.Equals(bootstrap.AdminUser, "sa", StringComparison.OrdinalIgnoreCase) && !bootstrap.AllowSaAdmin)
            {
                // A well-known name with every right is what attackers try first (DEC-032).
                errors.Add($"{admin}:AdminUser must not be 'sa': use a dedicated administrator login with CONTROL SERVER. " +
                    $"Only the bundled SQL Server deployment allows 'sa' ({admin}:AllowSaAdmin), until Goldmark generates its own administrator login.");
            }

            // The app container would hold a credential that also authenticates as the administrator.
            if (string.Equals(db.Password, bootstrap.AdminPassword, StringComparison.Ordinal))
            {
                errors.Add($"{database}:Password must differ from {admin}:AdminPassword.");
            }

            if (string.Equals(migration.Password, bootstrap.AdminPassword, StringComparison.Ordinal))
            {
                errors.Add($"{migrations}:Password must differ from {admin}:AdminPassword.");
            }
        }

        if (!string.IsNullOrEmpty(migration.Password) && string.Equals(migration.Password, db.Password, StringComparison.Ordinal))
        {
            // Otherwise the app container's credential would also authenticate as db_owner.
            errors.Add($"{migrations}:Password must differ from {database}:Password.");
        }

        errors.AddRange(CommonConnectionErrors(db));

        if (bootstrap.Mode is not (BootstrapOptions.CreateMode or BootstrapOptions.ExistingMode))
        {
            errors.Add($"{admin}:Mode must be '{BootstrapOptions.CreateMode}' or '{BootstrapOptions.ExistingMode}'.");
        }

        errors.AddRange(FindingPolicy.Errors(bootstrap.AcceptedFindings));

        if (bootstrap.WaitSeconds is < 0 or > 900)
        {
            errors.Add($"{admin}:WaitSeconds must be between 0 and 900.");
        }

        return errors;
    }

    /// <summary>Connection settings every database command needs.</summary>
    internal static IEnumerable<string> CommonConnectionErrors(DatabaseOptions db)
    {
        if (db.Encrypt is not ("Strict" or "Mandatory"))
        {
            yield return $"{DatabaseOptions.SectionName}:Encrypt must be 'Strict' or 'Mandatory'.";
        }

        foreach (var pin in DatabaseOptionsValidator.CertificatePinErrors(db.ServerCertificatePath))
        {
            yield return pin;
        }

        if (db.ConnectTimeoutSeconds is < 1 or > 120)
        {
            // 0 would mean "wait forever" to SqlClient.
            yield return $"{DatabaseOptions.SectionName}:ConnectTimeoutSeconds must be between 1 and 120.";
        }
    }

    private static void LoginErrors(List<string> errors, string setting, string login, string adminLogin)
    {
        if (!SqlIdentifier.IsValid(login))
        {
            errors.Add($"{setting} must be a simple identifier.");
        }
        else if (SqlIdentifier.IsReservedPrincipal(login))
        {
            errors.Add($"{setting} must not be 'sa' or a reserved database principal name (dbo, guest, sys, INFORMATION_SCHEMA, public, {RuntimeRole}).");
        }
        else if (string.Equals(login, adminLogin, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{setting} must differ from {BootstrapOptions.SectionName}:AdminUser.");
        }
    }
}

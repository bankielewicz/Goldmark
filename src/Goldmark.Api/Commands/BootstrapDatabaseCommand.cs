using Goldmark.Api.Configuration;
using Goldmark.Api.Data;
using Microsoft.Data.SqlClient;

namespace Goldmark.Api.Commands;

/// <summary>
/// <c>bootstrap-db</c>: one-shot, idempotent creation of the Goldmark database and the
/// least-privilege runtime login (slice 1 scope: CONNECT only, no schema). It runs with
/// administrative credentials that only the bootstrap container receives.
/// <para>
/// Goldmark only manages principals it created itself, recognised by a marker on the
/// database user. A runtime login or database user that already exists without that marker
/// is refused (exit 3) with nothing changed, unless the operator opts in to adopting it with
/// <see cref="BootstrapOptions.AdoptExistingLogin"/>. Login, user and marker are created in
/// one transaction. After provisioning, the login's effective permissions are checked as the
/// login itself. Migrations and the migration login arrive in slice 2.
/// </para>
/// </summary>
internal static class BootstrapDatabaseCommand
{
    public const string Name = "bootstrap-db";
    public const string ManagedMarker = "goldmark:managed";
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

    // Read-only. Run in the Goldmark database: user mapped to the login (by SID), whether that
    // user carries Goldmark's marker, and whether a user of that name belongs to someone else.
    private const string UserStateSql = """
        SET NOCOUNT ON;
        DECLARE @sid varbinary(85) = SUSER_SID(@login);
        DECLARE @uid int = (SELECT principal_id FROM sys.database_principals WHERE sid = @sid);
        SELECT
            CASE WHEN @uid IS NULL THEN 0 ELSE 1 END,
            CASE WHEN EXISTS (SELECT 1 FROM sys.extended_properties
                WHERE class = 4 AND major_id = @uid AND name = N'goldmark:managed') THEN 1 ELSE 0 END,
            CASE WHEN EXISTS (SELECT 1 FROM sys.database_principals
                WHERE name = @login AND (@sid IS NULL OR sid <> @sid)) THEN 1 ELSE 0 END;
        """;

    // Identifiers and the password are parameters, quoted server-side with QUOTENAME,
    // because DDL statements cannot take parameters directly.
    private const string CreateDatabaseSql = """
        DECLARE @sql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@db) + N';';
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

    // Run in the Goldmark database. Login, user and marker change together or not at all.
    // @adopt_user remaps an existing same-name user (adoption only).
    private const string ProvisionSql = """
        SET NOCOUNT ON;
        SET XACT_ABORT ON;
        DECLARE @sql nvarchar(max);
        BEGIN TRANSACTION;
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
            EXEC sys.sp_addextendedproperty @name = N'goldmark:managed', @value = N'runtime-login',
                @level0type = N'USER', @level0name = @user;
            SELECT N'marked database user as managed by Goldmark';
        END;
        COMMIT TRANSACTION;
        """;

    // Executed as the runtime login. Returns one row per role membership or effective
    // permission beyond the baseline that a fresh CONNECT-only login has on SQL Server 2025
    // (measured: server CONNECT SQL + VIEW ANY DATABASE; database CONNECT + the two VIEW ANY
    // COLUMN ... KEY DEFINITION grants that public holds). No rows = least privilege.
    // System views differ in collation, so every column is collated to the database default.
    internal const string VerifySql = """
        SET NOCOUNT ON;
        SELECT CAST(N'server role' AS nvarchar(32)) AS Kind, r.name COLLATE DATABASE_DEFAULT AS Name
        FROM sys.login_token AS t JOIN sys.server_principals AS r ON r.sid = t.sid
        WHERE t.type = N'SERVER ROLE' AND r.name <> N'public'
        UNION ALL
        SELECT N'database role', name COLLATE DATABASE_DEFAULT
        FROM sys.user_token WHERE type = N'ROLE' AND name <> N'public'
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

    public static async Task<int> RunAsync()
    {
        var configuration = CommandConfiguration.Build();
        var db = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
        var bootstrap = configuration.GetSection(BootstrapOptions.SectionName).Get<BootstrapOptions>() ?? new BootstrapOptions();

        var errors = Validate(db, bootstrap);
        if (errors.Count > 0)
        {
            await Console.Error.WriteLineAsync("bootstrap-db: invalid configuration:");
            foreach (var error in errors)
            {
                await Console.Error.WriteLineAsync($"  - {error}");
            }

            return 2;
        }

        var admin = SqlConnectionFactory.Build(db, bootstrap.AdminUser, bootstrap.AdminPassword, database: "master");
        admin.Pooling = false;
        try
        {
            await using var connection = await OpenWithRetryAsync(admin.ConnectionString, TimeSpan.FromSeconds(bootstrap.WaitSeconds));

            // 1. Read-only: decide everything before changing anything.
            var login = await LoginStateAsync(connection, db);
            var databaseExists = await ScalarAsync(connection, DatabaseExistsSql, db) == 1;
            var user = (Mapped: false, Managed: false, NameTakenByOther: false);
            if (databaseExists)
            {
                await connection.ChangeDatabaseAsync(db.Name);
                user = await UserStateAsync(connection, db);
                await connection.ChangeDatabaseAsync("master");
            }

            var managed = login is not null && user.Mapped && user.Managed;
            if (login is { Type: not "S" } nonSql)
            {
                return await RefuseAsync($"'{db.User}' exists but is not a SQL Server login (type {nonSql.Type}).");
            }

            if (!managed && !bootstrap.AdoptExistingLogin && (login is not null || user.NameTakenByOther))
            {
                var what = login is not null ? $"login '{db.User}'" : $"database user '{db.User}'";
                return await RefuseAsync(
                    $"the {what} already exists and was not created by Goldmark. Nothing was changed. " +
                    "Configure a different GOLDMARK_DB_USER, or set GOLDMARK_DB_ADOPT_EXISTING_LOGIN=true to take it over " +
                    "(its password is then set to GOLDMARK_DB_PASSWORD).");
            }

            if (bootstrap.AdoptExistingLogin && !managed && (login is not null || user.NameTakenByOther))
            {
                Report($"adopting the existing {(login is not null ? "login" : "database user")} '{db.User}' (GOLDMARK_DB_ADOPT_EXISTING_LOGIN=true)");
            }

            // 2. Provision. The password is checked against SQL Server's policy before the
            //    database is created; login, user and marker then change in one transaction.
            if (login is not { PasswordMatches: true })
            {
                await ExecuteAsync(connection, PasswordPolicyProbeSql, db, bootstrap);
            }

            if (!databaseExists)
            {
                await ExecuteAsync(connection, CreateDatabaseSql, db, bootstrap);
                Report("created database");
            }

            await connection.ChangeDatabaseAsync(db.Name);
            foreach (var step in await StepsAsync(connection, ProvisionSql, db, bootstrap))
            {
                Report(step);
            }

            // 3. Verify, as the runtime login, that it holds nothing beyond CONNECT.
            var runtime = SqlConnectionFactory.Build(db);
            runtime.Pooling = false;
            await using var appConnection = new SqlConnection(runtime.ConnectionString);
            await appConnection.OpenAsync();
            var excess = await FindingsAsync(appConnection, VerifySql);
            if (excess.Count > 0)
            {
                return await RefuseAsync(
                    $"the runtime login '{db.User}' has rights beyond CONNECT: {string.Join("; ", excess)}. Remove them.");
            }

            Report($"verified runtime login '{db.User}' has no role memberships or permissions beyond CONNECT.");
            return 0;
        }
        catch (SqlException ex)
        {
            await Console.Error.WriteLineAsync($"bootstrap-db: SQL Server error {ex.Number}: {ex.Message}");
            return 1;
        }
        catch (OperationCanceledException)
        {
            await Console.Error.WriteLineAsync(
                $"bootstrap-db: SQL Server did not accept connections within {bootstrap.WaitSeconds} s.");
            return 1;
        }
    }

    private static void Report(string message) => Console.WriteLine($"bootstrap-db: {message}");

    private static async Task<int> RefuseAsync(string reason)
    {
        await Console.Error.WriteLineAsync($"bootstrap-db: refusing to continue: {reason}");
        return Refused;
    }

    private static SqlCommand Command(SqlConnection connection, string sql, DatabaseOptions db, BootstrapOptions? bootstrap = null)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 60;
        command.Parameters.Add(new SqlParameter("@db", System.Data.SqlDbType.NVarChar, 128) { Value = db.Name });
        command.Parameters.Add(new SqlParameter("@login", System.Data.SqlDbType.NVarChar, 128) { Value = db.User });
        command.Parameters.Add(new SqlParameter("@password", System.Data.SqlDbType.NVarChar, 128) { Value = db.Password });
        command.Parameters.Add(new SqlParameter("@adopt_user", System.Data.SqlDbType.Bit) { Value = bootstrap?.AdoptExistingLogin ?? false });
        return command;
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql, DatabaseOptions db, BootstrapOptions bootstrap)
    {
        await using var command = Command(connection, sql, db, bootstrap);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ScalarAsync(SqlConnection connection, string sql, DatabaseOptions db)
    {
        await using var command = Command(connection, sql, db);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private readonly record struct LoginState(string Type, bool PasswordMatches);

    private static async Task<LoginState?> LoginStateAsync(SqlConnection connection, DatabaseOptions db)
    {
        await using var command = Command(connection, LoginStateSql, db);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? new LoginState(reader.GetString(0).Trim(), reader.GetInt32(1) == 1) : null;
    }

    private static async Task<(bool Mapped, bool Managed, bool NameTakenByOther)> UserStateAsync(SqlConnection connection, DatabaseOptions db)
    {
        await using var command = Command(connection, UserStateSql, db);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetInt32(0) == 1, reader.GetInt32(1) == 1, reader.GetInt32(2) == 1);
    }

    private static async Task<List<string>> StepsAsync(SqlConnection connection, string sql, DatabaseOptions db, BootstrapOptions bootstrap)
    {
        await using var command = Command(connection, sql, db, bootstrap);
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

    private static async Task<List<string>> FindingsAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var findings = new List<string>();
        while (await reader.ReadAsync())
        {
            findings.Add($"{reader.GetString(0)} {reader.GetString(1)}");
        }

        return findings;
    }

    internal static List<string> Validate(DatabaseOptions db, BootstrapOptions bootstrap)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(db.Host))
        {
            errors.Add($"{DatabaseOptions.SectionName}:Host is required.");
        }

        if (!SqlIdentifier.IsValid(db.Name))
        {
            errors.Add($"{DatabaseOptions.SectionName}:Name must be a simple identifier.");
        }
        else if (SqlIdentifier.IsSystemDatabase(db.Name))
        {
            errors.Add($"{DatabaseOptions.SectionName}:Name must not be a SQL Server system database.");
        }

        if (!SqlIdentifier.IsValid(db.User) || string.Equals(db.User, "sa", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{DatabaseOptions.SectionName}:User must be a simple identifier and not 'sa'.");
        }
        else if (string.Equals(db.User, bootstrap.AdminUser, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{DatabaseOptions.SectionName}:User must differ from {BootstrapOptions.SectionName}:AdminUser.");
        }

        if (db.Password.Length is < 12 or > MaxPasswordLength)
        {
            errors.Add($"{DatabaseOptions.SectionName}:Password must be 12 to {MaxPasswordLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(bootstrap.AdminUser) || string.IsNullOrEmpty(bootstrap.AdminPassword))
        {
            errors.Add($"{BootstrapOptions.SectionName}:AdminUser and AdminPassword are required.");
        }
        else if (string.Equals(db.Password, bootstrap.AdminPassword, StringComparison.Ordinal))
        {
            // The app container would hold a credential that also authenticates as the administrator.
            errors.Add($"{DatabaseOptions.SectionName}:Password must differ from {BootstrapOptions.SectionName}:AdminPassword.");
        }

        if (db.Encrypt is not ("Strict" or "Mandatory"))
        {
            errors.Add($"{DatabaseOptions.SectionName}:Encrypt must be 'Strict' or 'Mandatory'.");
        }

        if (db.ConnectTimeoutSeconds is < 1 or > 120)
        {
            // 0 would mean "wait forever" to SqlClient.
            errors.Add($"{DatabaseOptions.SectionName}:ConnectTimeoutSeconds must be between 1 and 120.");
        }

        if (bootstrap.WaitSeconds is < 0 or > 900)
        {
            errors.Add($"{BootstrapOptions.SectionName}:WaitSeconds must be between 0 and 900.");
        }

        return errors;
    }

    /// <summary>
    /// Retries until SQL Server accepts the connection. The whole wait, including each
    /// connection attempt, is bounded by <paramref name="wait"/> plus one connect timeout.
    /// </summary>
    private static async Task<SqlConnection> OpenWithRetryAsync(string connectionString, TimeSpan wait)
    {
        var connectTimeout = TimeSpan.FromSeconds(new SqlConnectionStringBuilder(connectionString).ConnectTimeout);
        using var overall = new CancellationTokenSource(wait + connectTimeout);
        var deadline = DateTime.UtcNow + wait;
        while (true)
        {
            var connection = new SqlConnection(connectionString);
            try
            {
                await connection.OpenAsync(overall.Token);
                return connection;
            }
            catch (SqlException ex) when (DateTime.UtcNow < deadline)
            {
                await connection.DisposeAsync();
                Console.WriteLine($"bootstrap-db: SQL Server not ready yet (error {ex.Number}); retrying");
                await Task.Delay(TimeSpan.FromSeconds(3), overall.Token);
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }
        }
    }
}

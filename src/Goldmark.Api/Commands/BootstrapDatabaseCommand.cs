using Goldmark.Api.Configuration;
using Goldmark.Api.Data;
using Microsoft.Data.SqlClient;

namespace Goldmark.Api.Commands;

/// <summary>
/// <c>bootstrap-db</c>: one-shot, idempotent creation of the Goldmark database and the
/// least-privilege runtime login (slice 1 scope only: no schema, no permissions beyond
/// CONNECT). Runs with administrative credentials that only the bootstrap container receives.
/// <para>
/// An existing login is inspected <b>before anything is changed</b>. It must be an enabled SQL
/// login with no rights beyond the CONNECT-only baseline at any scope. Its password is only
/// changed when rotation is explicitly requested (<see cref="BootstrapOptions.RotatePassword"/>),
/// so a name that collides with another account never gets its password replaced. After
/// provisioning, the login is checked again, both from the catalog and by its own effective
/// permissions. Domain migrations and the separate migration login arrive in slice 2.
/// </para>
/// </summary>
internal static class BootstrapDatabaseCommand
{
    public const string Name = "bootstrap-db";
    private const int MaxPasswordLength = 128; // QUOTENAME input limit; longer input would yield NULL.
    private const int Refused = 3;

    // Identifiers and the password are passed as parameters and quoted server-side with
    // QUOTENAME, because DDL statements cannot take parameters directly.
    private const string EnsureDatabaseSql = """
        SET NOCOUNT ON;
        IF DB_ID(@db) IS NULL
        BEGIN
            DECLARE @sql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@db) + N';';
            EXEC sys.sp_executesql @sql;
            SELECT 1;
        END
        ELSE SELECT 0;
        """;

    // Type ('S' = SQL login), disabled flag and whether the configured password already matches.
    private const string LoginStateSql = """
        SET NOCOUNT ON;
        SELECT CAST(p.type AS nvarchar(2)), CAST(p.is_disabled AS int),
            CAST(ISNULL(PWDCOMPARE(@password, l.password_hash), 0) AS int)
        FROM sys.server_principals AS p LEFT JOIN sys.sql_logins AS l ON l.principal_id = p.principal_id
        WHERE p.name = @login;
        """;

    private const string CreateLoginSql = """
        DECLARE @sql nvarchar(max) = N'CREATE LOGIN ' + QUOTENAME(@login)
            + N' WITH PASSWORD = ' + QUOTENAME(@password, N'''')
            + N', DEFAULT_DATABASE = ' + QUOTENAME(@db)
            + N', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;';
        EXEC sys.sp_executesql @sql;
        """;

    private const string RotatePasswordSql = """
        DECLARE @sql nvarchar(max) = N'ALTER LOGIN ' + QUOTENAME(@login)
            + N' WITH PASSWORD = ' + QUOTENAME(@password, N'''') + N';';
        EXEC sys.sp_executesql @sql;
        """;

    // Runs in the Goldmark database.
    private const string EnsureUserSql = """
        SET NOCOUNT ON;
        IF USER_ID(@login) IS NULL
        BEGIN
            DECLARE @sql nvarchar(max) = N'CREATE USER ' + QUOTENAME(@login) + N' FOR LOGIN ' + QUOTENAME(@login) + N';';
            EXEC sys.sp_executesql @sql;
            SELECT 1;
        END
        ELSE SELECT 0;
        """;

    /// <summary>
    /// Catalog inspection, run by the administrator in the Goldmark database. Returns one row
    /// per right the login holds beyond the baseline measured for a fresh CONNECT-only login on
    /// SQL Server 2025, at every scope:
    /// <list type="bullet">
    /// <item>role memberships;</item>
    /// <item>explicit grants on the server, endpoints, logins, the database, schemas, objects and users;</item>
    /// <item>ownership of anything;</item>
    /// <item>mappings into other databases;</item>
    /// <item>non-default grants to public, which every login inherits.</item>
    /// </list>
    /// DENYs are ignored because they only remove rights. Slice 2 adds the runtime DML grants
    /// to the baseline.
    /// </summary>
    internal const string InspectSql = """
        SET NOCOUNT ON;
        DECLARE @sid varbinary(85) = SUSER_SID(@login);
        DECLARE @uid int = (SELECT principal_id FROM sys.database_principals WHERE sid = @sid);
        DECLARE @public_server int = (SELECT principal_id FROM sys.server_principals WHERE name = N'public' AND type = N'R');
        DECLARE @findings TABLE (Kind nvarchar(64), Detail nvarchar(512));

        IF @sid IS NOT NULL
        BEGIN
            INSERT @findings SELECT N'server role', r.name
            FROM sys.server_role_members AS m
            JOIN sys.server_principals AS r ON r.principal_id = m.role_principal_id
            JOIN sys.server_principals AS p ON p.principal_id = m.member_principal_id
            WHERE p.sid = @sid;

            INSERT @findings SELECT N'server permission',
                CONCAT(sp.state_desc, N' ', sp.permission_name, N' ON ', sp.class_desc, N' #', sp.major_id)
            FROM sys.server_permissions AS sp
            JOIN sys.server_principals AS p ON p.principal_id = sp.grantee_principal_id
            WHERE p.sid = @sid AND sp.state IN ('G', 'W')
              AND NOT (sp.class = 100 AND sp.permission_name = N'CONNECT SQL');

            INSERT @findings SELECT N'owns', CONCAT(N'server principal ', o.name)
            FROM sys.server_principals AS o JOIN sys.server_principals AS p ON p.principal_id = o.owning_principal_id
            WHERE p.sid = @sid;

            INSERT @findings SELECT N'owns', CONCAT(N'endpoint ', e.name)
            FROM sys.endpoints AS e JOIN sys.server_principals AS p ON p.principal_id = e.principal_id
            WHERE p.sid = @sid;

            INSERT @findings SELECT N'owns', CONCAT(N'database ', d.name) FROM sys.databases AS d WHERE d.owner_sid = @sid;

            DECLARE @other sysname, @probe nvarchar(max), @mapped int;
            DECLARE databases CURSOR LOCAL FAST_FORWARD FOR
                SELECT name FROM sys.databases WHERE state = 0 AND database_id <> DB_ID();
            OPEN databases;
            FETCH NEXT FROM databases INTO @other;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                SET @probe = N'SELECT @mapped = COUNT(*) FROM ' + QUOTENAME(@other) + N'.sys.database_principals WHERE sid = @sid;';
                EXEC sys.sp_executesql @probe, N'@sid varbinary(85), @mapped int OUTPUT', @sid = @sid, @mapped = @mapped OUTPUT;
                IF @mapped > 0 INSERT @findings VALUES (N'mapped into database', @other);
                FETCH NEXT FROM databases INTO @other;
            END;
            CLOSE databases;
            DEALLOCATE databases;
        END;

        INSERT @findings SELECT N'server permission (public)',
            CONCAT(sp.state_desc, N' ', sp.permission_name, N' ON ', sp.class_desc, N' #', sp.major_id)
        FROM sys.server_permissions AS sp
        WHERE sp.grantee_principal_id = @public_server AND sp.state IN ('G', 'W')
          AND NOT (sp.class = 100 AND sp.permission_name = N'VIEW ANY DATABASE')
          AND NOT (sp.class = 105 AND sp.permission_name = N'CONNECT' AND sp.major_id < 65536);

        IF @uid IS NOT NULL
        BEGIN
            INSERT @findings SELECT N'database role', r.name
            FROM sys.database_role_members AS m JOIN sys.database_principals AS r ON r.principal_id = m.role_principal_id
            WHERE m.member_principal_id = @uid;

            INSERT @findings SELECT N'database permission',
                CONCAT(dp.state_desc, N' ', dp.permission_name, N' ON ', dp.class_desc, N' #', dp.major_id)
            FROM sys.database_permissions AS dp
            WHERE dp.grantee_principal_id = @uid AND dp.state IN ('G', 'W')
              AND NOT (dp.class = 0 AND dp.permission_name = N'CONNECT');

            INSERT @findings SELECT N'owns', CONCAT(N'schema ', s.name) FROM sys.schemas AS s WHERE s.principal_id = @uid;
            INSERT @findings SELECT N'owns', CONCAT(N'database principal ', o.name)
            FROM sys.database_principals AS o WHERE o.owning_principal_id = @uid;
            INSERT @findings SELECT N'owns', CONCAT(N'object ', o.name) FROM sys.objects AS o WHERE o.principal_id = @uid;
        END;

        INSERT @findings SELECT N'database permission (public)',
            CONCAT(dp.state_desc, N' ', dp.permission_name, N' ON ', dp.class_desc, N' #', dp.major_id)
        FROM sys.database_permissions AS dp
        WHERE dp.grantee_principal_id = DATABASE_PRINCIPAL_ID(N'public') AND dp.state IN ('G', 'W')
          AND NOT (dp.class = 0 AND dp.permission_name IN
              (N'VIEW ANY COLUMN ENCRYPTION KEY DEFINITION', N'VIEW ANY COLUMN MASTER KEY DEFINITION'))
          AND NOT (dp.class = 1 AND dp.major_id < 0 AND dp.permission_name = N'SELECT');

        SELECT Kind, Detail FROM @findings;
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

            if (await ScalarAsync(connection, EnsureDatabaseSql, db) == 1)
            {
                Report("created database");
            }

            // 1. An existing login is inspected before anything about it is changed.
            var state = await LoginStateAsync(connection, db);
            if (state is { } existing)
            {
                if (existing.Type != "S")
                {
                    return await RefuseAsync($"'{db.User}' exists but is not a SQL Server login (type {existing.Type}). Nothing was changed.");
                }

                if (existing.Disabled)
                {
                    return await RefuseAsync($"the existing login '{db.User}' is disabled; Goldmark does not re-enable logins. Nothing was changed.");
                }

                await connection.ChangeDatabaseAsync(db.Name);
                var findings = await FindingsAsync(connection, InspectSql, db);
                await connection.ChangeDatabaseAsync("master");
                if (findings.Count > 0)
                {
                    return await RefuseAsync(
                        $"the existing login '{db.User}' has rights beyond CONNECT: {string.Join("; ", findings)}. " +
                        "Nothing was changed. Remove those rights, or configure a different login.");
                }

                if (!existing.PasswordMatches)
                {
                    if (!bootstrap.RotatePassword)
                    {
                        return await RefuseAsync(
                            $"the existing login '{db.User}' has a different password. Nothing was changed. " +
                            "To rotate it, run once with GOLDMARK_DB_ROTATE_PASSWORD=true (Goldmark:Bootstrap:RotatePassword).");
                    }

                    await ExecuteAsync(connection, RotatePasswordSql, db);
                    Report("rotated login password");
                }
            }
            else
            {
                await ExecuteAsync(connection, CreateLoginSql, db);
                Report("created login");
            }

            // 2. Map the login into the Goldmark database.
            await connection.ChangeDatabaseAsync(db.Name);
            if (await ScalarAsync(connection, EnsureUserSql, db) == 1)
            {
                Report("created database user");
            }

            // 3. Verify the result from the catalog and from the login's own effective permissions.
            var catalog = await FindingsAsync(connection, InspectSql, db);
            var runtime = SqlConnectionFactory.Build(db);
            runtime.Pooling = false;
            await using var appConnection = new SqlConnection(runtime.ConnectionString);
            await appConnection.OpenAsync();
            var effective = await FindingsAsync(appConnection, VerifySql, db);
            var excess = catalog.Concat(effective).Distinct().ToList();
            if (excess.Count > 0)
            {
                return await RefuseAsync(
                    $"the runtime login '{db.User}' has rights beyond CONNECT: {string.Join("; ", excess)}. Remove them.");
            }

            Report($"verified runtime login '{db.User}' has no role memberships, grants or ownership beyond CONNECT.");
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
        // Refuse rather than repair: such rights or accounts were set up outside Goldmark.
        await Console.Error.WriteLineAsync($"bootstrap-db: refusing to continue: {reason}");
        return Refused;
    }

    private static SqlCommand Command(SqlConnection connection, string sql, DatabaseOptions db)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 60;
        command.Parameters.Add(new SqlParameter("@db", System.Data.SqlDbType.NVarChar, 128) { Value = db.Name });
        command.Parameters.Add(new SqlParameter("@login", System.Data.SqlDbType.NVarChar, 128) { Value = db.User });
        command.Parameters.Add(new SqlParameter("@password", System.Data.SqlDbType.NVarChar, 128) { Value = db.Password });
        return command;
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql, DatabaseOptions db)
    {
        await using var command = Command(connection, sql, db);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ScalarAsync(SqlConnection connection, string sql, DatabaseOptions db)
    {
        await using var command = Command(connection, sql, db);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<(string Type, bool Disabled, bool PasswordMatches)?> LoginStateAsync(
        SqlConnection connection, DatabaseOptions db)
    {
        await using var command = Command(connection, LoginStateSql, db);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return (reader.GetString(0).Trim(), reader.GetInt32(1) == 1, reader.GetInt32(2) == 1);
    }

    private static async Task<List<string>> FindingsAsync(SqlConnection connection, string sql, DatabaseOptions db)
    {
        await using var command = Command(connection, sql, db);
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

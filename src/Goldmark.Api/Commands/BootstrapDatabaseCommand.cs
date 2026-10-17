using Goldmark.Api.Configuration;
using Goldmark.Api.Data;
using Microsoft.Data.SqlClient;

namespace Goldmark.Api.Commands;

/// <summary>
/// <c>bootstrap-db</c>: one-shot, idempotent creation of the Goldmark database and the
/// least-privilege runtime login (slice 1 scope only: no schema, no permissions beyond
/// CONNECT). Runs with administrative credentials that only the bootstrap container receives,
/// then verifies, connected as the runtime login, that the login is not privileged.
/// Domain migrations and the separate migration login arrive in slice 2.
/// </summary>
internal static class BootstrapDatabaseCommand
{
    public const string Name = "bootstrap-db";
    private const int MaxPasswordLength = 128; // QUOTENAME input limit; longer input would yield NULL.

    // Identifiers and the password are passed as parameters and quoted server-side with
    // QUOTENAME, because DDL statements cannot take parameters directly.
    private const string ProvisionSql = """
        SET NOCOUNT ON;
        DECLARE @sql nvarchar(max);
        IF DB_ID(@db) IS NULL
        BEGIN
            SET @sql = N'CREATE DATABASE ' + QUOTENAME(@db) + N';';
            EXEC sys.sp_executesql @sql;
            SELECT N'created database' AS step;
        END;
        IF SUSER_ID(@login) IS NULL
        BEGIN
            SET @sql = N'CREATE LOGIN ' + QUOTENAME(@login)
                + N' WITH PASSWORD = ' + QUOTENAME(@password, N'''')
                + N', DEFAULT_DATABASE = ' + QUOTENAME(@db)
                + N', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;';
            EXEC sys.sp_executesql @sql;
            SELECT N'created login' AS step;
        END
        ELSE
        BEGIN
            SET @sql = N'ALTER LOGIN ' + QUOTENAME(@login) + N' WITH PASSWORD = ' + QUOTENAME(@password, N'''') + N'; '
                + N'ALTER LOGIN ' + QUOTENAME(@login) + N' ENABLE;';
            EXEC sys.sp_executesql @sql;
            SELECT N'updated login password' AS step;
        END;
        SET @sql = N'USE ' + QUOTENAME(@db) + N';
            IF USER_ID(@login) IS NULL
            BEGIN
                DECLARE @create nvarchar(max) = N''CREATE USER '' + QUOTENAME(@login) + N'' FOR LOGIN '' + QUOTENAME(@login) + N'';'';
                EXEC sys.sp_executesql @create;
                SELECT N''created database user'' AS step;
            END;';
        EXEC sys.sp_executesql @sql, N'@login sysname', @login = @login;
        IF IS_SRVROLEMEMBER(N'sysadmin', @login) = 1
            THROW 50001, N'The runtime login is a sysadmin; refusing to continue.', 1;
        """;

    // Executed as the runtime login. Returns one row per role membership or effective
    // permission beyond the baseline that a fresh CONNECT-only login has on SQL Server 2025
    // (measured: server CONNECT SQL + VIEW ANY DATABASE; database CONNECT + the two VIEW ANY
    // COLUMN ... KEY DEFINITION grants that public holds). No rows = least privilege.
    // Slice 2 extends the database allow-list with the runtime DML grants.
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
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = ProvisionSql;
                command.CommandTimeout = 60;
                command.Parameters.Add(new SqlParameter("@db", System.Data.SqlDbType.NVarChar, 128) { Value = db.Name });
                command.Parameters.Add(new SqlParameter("@login", System.Data.SqlDbType.NVarChar, 128) { Value = db.User });
                command.Parameters.Add(new SqlParameter("@password", System.Data.SqlDbType.NVarChar, 128) { Value = db.Password });
                await using var reader = await command.ExecuteReaderAsync();
                do
                {
                    while (await reader.ReadAsync())
                    {
                        Console.WriteLine($"bootstrap-db: {reader.GetString(0)}");
                    }
                }
                while (await reader.NextResultAsync());
            }

            var runtime = SqlConnectionFactory.Build(db);
            runtime.Pooling = false;
            await using var appConnection = new SqlConnection(runtime.ConnectionString);
            await appConnection.OpenAsync();
            await using var verify = appConnection.CreateCommand();
            verify.CommandText = VerifySql;
            await using var result = await verify.ExecuteReaderAsync();
            var excess = new List<string>();
            while (await result.ReadAsync())
            {
                excess.Add($"{result.GetString(0)} {result.GetString(1)}");
            }

            if (excess.Count > 0)
            {
                // Refuse rather than revoke: these rights were granted outside Goldmark.
                await Console.Error.WriteLineAsync(
                    $"bootstrap-db: refusing to continue: the runtime login '{db.User}' has rights beyond CONNECT: " +
                    $"{string.Join("; ", excess)}. Remove them, or use a different login.");
                return 3;
            }

            Console.WriteLine($"bootstrap-db: verified runtime login '{db.User}' has no role memberships or permissions beyond CONNECT.");
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

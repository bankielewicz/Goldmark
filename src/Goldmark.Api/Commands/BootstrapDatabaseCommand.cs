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

    // Executed as the runtime login. Every column must be 0.
    private const string VerifySql = """
        SELECT
            ISNULL(IS_SRVROLEMEMBER(N'sysadmin'), 0) AS IsSysadmin,
            ISNULL(IS_MEMBER(N'db_owner'), 0) AS IsDbOwner,
            ISNULL(IS_MEMBER(N'db_ddladmin'), 0) AS IsDdlAdmin,
            ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'CREATE TABLE'), 0) AS CanCreateTable,
            ISNULL(HAS_PERMS_BY_NAME(NULL, NULL, N'CONTROL SERVER'), 0) AS HasControlServer;
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
            await result.ReadAsync();
            var privileged = Enumerable.Range(0, result.FieldCount)
                .Where(i => Convert.ToInt32(result.GetValue(i), System.Globalization.CultureInfo.InvariantCulture) != 0)
                .Select(result.GetName)
                .ToList();
            if (privileged.Count > 0)
            {
                await Console.Error.WriteLineAsync(
                    $"bootstrap-db: the runtime login '{db.User}' is over-privileged ({string.Join(", ", privileged)}). Remove those rights.");
                return 3;
            }

            Console.WriteLine($"bootstrap-db: verified runtime login '{db.User}' can connect and has no elevated rights.");
            return 0;
        }
        catch (SqlException ex)
        {
            await Console.Error.WriteLineAsync($"bootstrap-db: SQL Server error {ex.Number}: {ex.Message}");
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

        if (bootstrap.WaitSeconds is < 0 or > 900)
        {
            errors.Add($"{BootstrapOptions.SectionName}:WaitSeconds must be between 0 and 900.");
        }

        return errors;
    }

    private static async Task<SqlConnection> OpenWithRetryAsync(string connectionString, TimeSpan wait)
    {
        var deadline = DateTime.UtcNow + wait;
        while (true)
        {
            var connection = new SqlConnection(connectionString);
            try
            {
                await connection.OpenAsync();
                return connection;
            }
            catch (SqlException ex) when (DateTime.UtcNow < deadline)
            {
                await connection.DisposeAsync();
                Console.WriteLine($"bootstrap-db: SQL Server not ready yet (error {ex.Number}); retrying");
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }
}

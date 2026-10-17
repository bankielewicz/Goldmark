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

    // Type ('S' = SQL login), disabled, password match, and whether the login can stop working
    // on its own (expiration policy, expired, locked out, must change password).
    private const string LoginStateSql = """
        SET NOCOUNT ON;
        SELECT CAST(p.type AS nvarchar(2)), CAST(p.is_disabled AS int),
            CAST(ISNULL(PWDCOMPARE(@password, l.password_hash), 0) AS int),
            CAST(ISNULL(l.is_expiration_checked, 0) AS int),
            CAST(ISNULL(l.is_policy_checked, 0) AS int),
            CAST(ISNULL(LOGINPROPERTY(p.name, 'IsExpired'), 0) AS int),
            CAST(ISNULL(LOGINPROPERTY(p.name, 'IsLocked'), 0) AS int),
            CAST(ISNULL(LOGINPROPERTY(p.name, 'IsMustChange'), 0) AS int)
        FROM sys.server_principals AS p LEFT JOIN sys.sql_logins AS l ON l.principal_id = p.principal_id
        WHERE p.name = @login;
        """;

    private const string CreateLoginSql = """
        DECLARE @sql nvarchar(max) = N'CREATE LOGIN ' + QUOTENAME(@login)
            + N' WITH PASSWORD = ' + QUOTENAME(@password, N'''')
            + N', DEFAULT_DATABASE = [master], CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;';
        EXEC sys.sp_executesql @sql;
        """;

    // Run after the database exists, for a login created by this run only.
    private const string SetDefaultDatabaseSql = """
        DECLARE @sql nvarchar(max) = N'ALTER LOGIN ' + QUOTENAME(@login) + N' WITH DEFAULT_DATABASE = ' + QUOTENAME(@db) + N';';
        EXEC sys.sp_executesql @sql;
        """;

    private const string RotatePasswordSql = """
        DECLARE @sql nvarchar(max) = N'ALTER LOGIN ' + QUOTENAME(@login)
            + N' WITH PASSWORD = ' + QUOTENAME(@password, N'''') + N';';
        EXEC sys.sp_executesql @sql;
        """;

    // Runs in the Goldmark database. Matches the user by SID, not name: 0 = already mapped,
    // 1 = created, 2 = a user with this name belongs to a different login (never remapped).
    private const string EnsureUserSql = """
        SET NOCOUNT ON;
        IF EXISTS (SELECT 1 FROM sys.database_principals WHERE sid = SUSER_SID(@login)) SELECT 0;
        ELSE IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @login) SELECT 2;
        ELSE
        BEGIN
            DECLARE @sql nvarchar(max) = N'CREATE USER ' + QUOTENAME(@login) + N' FOR LOGIN ' + QUOTENAME(@login) + N';';
            EXEC sys.sp_executesql @sql;
            SELECT 1;
        END;
        """;

    private const string DatabaseExistsSql = "SELECT CASE WHEN DB_ID(@db) IS NULL THEN 0 ELSE 1 END;";

    /// <summary>
    /// Catalog inspection, run by the administrator in the Goldmark database (or in master while
    /// that database does not exist yet). Returns one row per right the login holds beyond the
    /// baseline measured for a fresh CONNECT-only login on SQL Server 2025, at every scope:
    /// <list type="bullet">
    /// <item>role memberships;</item>
    /// <item>explicit grants on the server, endpoints, logins, the database, schemas, objects and users;</item>
    /// <item>ownership of every securable catalog that records an owner, server and database;</item>
    /// <item>linked-server, remote-login and credential mappings, including wildcard mappings for all logins;</item>
    /// <item>guest access enabled in any database other than master, tempdb and msdb;</item>
    /// <item>mappings into other databases;</item>
    /// <item>non-default grants to public, which every login inherits;</item>
    /// <item>a database user with the login's name but another SID.</item>
    /// </list>
    /// DENYs are ignored because they only remove rights. Slice 2 adds the runtime DML grants
    /// to the baseline. The query was validated against SQL Server 2025 (see the smoke test).
    /// </summary>
    internal const string InspectSql = """
        SET NOCOUNT ON;
        DECLARE @sid varbinary(85) = SUSER_SID(@login);
        DECLARE @in_target bit = CASE WHEN DB_NAME() = @db THEN 1 ELSE 0 END;
        DECLARE @uid int = CASE WHEN @in_target = 1 THEN (SELECT principal_id FROM sys.database_principals WHERE sid = @sid) END;
        DECLARE @public_server int = (SELECT principal_id FROM sys.server_principals WHERE name = N'public' AND type = N'R');
        DECLARE @findings TABLE (Kind nvarchar(64), Detail nvarchar(512));

        IF @sid IS NOT NULL
        BEGIN
            DECLARE @pid int = (SELECT principal_id FROM sys.server_principals WHERE sid = @sid);

            INSERT @findings SELECT N'server role', r.name
            FROM sys.server_role_members AS m JOIN sys.server_principals AS r ON r.principal_id = m.role_principal_id
            WHERE m.member_principal_id = @pid;

            INSERT @findings SELECT N'server permission',
                CONCAT(sp.state_desc, N' ', sp.permission_name, N' ON ', sp.class_desc, N' #', sp.major_id)
            FROM sys.server_permissions AS sp
            WHERE sp.grantee_principal_id = @pid AND sp.state IN ('G', 'W')
              AND NOT (sp.class = 100 AND sp.permission_name = N'CONNECT SQL');

            -- Most DENYs only remove rights and are ignored, but a denied connection would make
            -- the run fail after provisioning; refuse it up front instead.
            INSERT @findings SELECT N'connection denied', CONCAT(N'DENY ', sp.permission_name, N' ON ', sp.class_desc)
            FROM sys.server_permissions AS sp
            WHERE sp.grantee_principal_id IN (@pid, @public_server) AND sp.state = 'D'
              AND sp.class = 100 AND sp.permission_name = N'CONNECT SQL';

            -- Ownership of, or mappings from, server-level securables.
            INSERT @findings SELECT N'owns', CONCAT(owned.kind, N' ', owned.name) FROM (
                SELECT N'server principal' AS kind, name COLLATE DATABASE_DEFAULT AS name, owning_principal_id AS owner FROM sys.server_principals
                UNION ALL SELECT N'endpoint', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.endpoints
                UNION ALL SELECT N'server event notification', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.server_event_notifications
                UNION ALL SELECT N'server audit', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.server_audits
            ) AS owned WHERE owned.owner = @pid;
            INSERT @findings SELECT N'owns', CONCAT(N'database ', d.name) FROM sys.databases AS d WHERE d.owner_sid = @sid;
            INSERT @findings SELECT N'owns', CONCAT(N'availability replica ', r.replica_server_name)
            FROM sys.availability_replicas AS r WHERE r.owner_sid = @sid;
            INSERT @findings SELECT N'linked server login mapping', CONCAT(N'server #', l.server_id)
            FROM sys.linked_logins AS l WHERE l.local_principal_id = @pid;
            INSERT @findings SELECT N'remote login mapping', CONCAT(N'server #', l.server_id)
            FROM sys.remote_logins AS l WHERE l.local_principal_id = @pid;
            INSERT @findings SELECT N'credential mapping', CONCAT(N'credential #', c.credential_id)
            FROM sys.server_principal_credentials AS c WHERE c.principal_id = @pid;
            INSERT @findings SELECT N'credential mapping', CONCAT(N'credential #', l.credential_id)
            FROM sys.sql_logins AS l WHERE l.principal_id = @pid AND l.credential_id IS NOT NULL;

            -- Mappings into any other database (the target database is inspected below).
            DECLARE @other sysname, @probe nvarchar(max), @mapped int;
            DECLARE databases CURSOR LOCAL FAST_FORWARD FOR
                SELECT name FROM sys.databases WHERE state = 0 AND name <> @db;
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

        -- Wildcard mappings (local principal 0) apply to every login, including this one. Only
        -- those that supply a shared remote credential add rights; self-credential mappings
        -- (SQL Server's default for every linked server, including the local entry) do not.
        INSERT @findings SELECT N'linked server login mapping', CONCAT(N'server #', l.server_id, N' (all logins)')
        FROM sys.linked_logins AS l WHERE l.local_principal_id = 0 AND l.uses_self_credential = 0;
        INSERT @findings SELECT N'remote login mapping', CONCAT(N'server #', l.server_id, N' (all logins)')
        FROM sys.remote_logins AS l WHERE l.local_principal_id = 0;

        -- guest access lets any login into a database without a mapping. It is enabled by
        -- default only in master, tempdb and msdb; anywhere else (model, user databases) it is
        -- a right this login would hold.
        DECLARE @guest_db sysname, @guest_probe nvarchar(max), @guest int;
        DECLARE guest_databases CURSOR LOCAL FAST_FORWARD FOR
            SELECT name FROM sys.databases WHERE state = 0 AND database_id NOT IN (1, 2, 4);
        OPEN guest_databases;
        FETCH NEXT FROM guest_databases INTO @guest_db;
        WHILE @@FETCH_STATUS = 0
        BEGIN
            SET @guest_probe = N'SELECT @guest = COUNT(*) FROM ' + QUOTENAME(@guest_db)
                + N'.sys.database_permissions WHERE grantee_principal_id = 2 AND class = 0'
                + N' AND permission_name = N''CONNECT'' AND state IN (''G'', ''W'');';
            EXEC sys.sp_executesql @guest_probe, N'@guest int OUTPUT', @guest = @guest OUTPUT;
            IF @guest > 0 INSERT @findings VALUES (N'guest access enabled in database', @guest_db);
            FETCH NEXT FROM guest_databases INTO @guest_db;
        END;
        CLOSE guest_databases;
        DEALLOCATE guest_databases;

        IF @in_target = 1
        BEGIN
            -- A user with the login's name but another SID (e.g. a restored database) is a collision.
            INSERT @findings SELECT N'user name collision', CONCAT(N'database user ', name, N' belongs to a different login')
            FROM sys.database_principals WHERE name = @login AND (@sid IS NULL OR sid <> @sid);

            INSERT @findings SELECT N'database permission (public)',
                CONCAT(dp.state_desc, N' ', dp.permission_name, N' ON ', dp.class_desc, N' #', dp.major_id)
            FROM sys.database_permissions AS dp
            WHERE dp.grantee_principal_id = DATABASE_PRINCIPAL_ID(N'public') AND dp.state IN ('G', 'W')
              AND NOT (dp.class = 0 AND dp.permission_name IN
                  (N'VIEW ANY COLUMN ENCRYPTION KEY DEFINITION', N'VIEW ANY COLUMN MASTER KEY DEFINITION'))
              AND NOT (dp.class = 1 AND dp.major_id < 0 AND dp.permission_name = N'SELECT');
        END;

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

            INSERT @findings SELECT N'connection denied', CONCAT(N'DENY ', dp.permission_name, N' ON ', dp.class_desc)
            FROM sys.database_permissions AS dp
            WHERE dp.grantee_principal_id IN (@uid, DATABASE_PRINCIPAL_ID(N'public')) AND dp.state = 'D'
              AND dp.class = 0 AND dp.permission_name = N'CONNECT';

            -- Ownership of every database securable that records an owner.
            INSERT @findings SELECT N'owns', CONCAT(owned.kind, N' ', owned.name) FROM (
                SELECT N'object' AS kind, name COLLATE DATABASE_DEFAULT AS name, principal_id AS owner FROM sys.objects
                UNION ALL SELECT N'schema', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.schemas
                UNION ALL SELECT N'database principal', name COLLATE DATABASE_DEFAULT, owning_principal_id FROM sys.database_principals
                UNION ALL SELECT N'type', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.types
                UNION ALL SELECT N'xml schema collection', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.xml_schema_collections
                UNION ALL SELECT N'assembly', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.assemblies
                UNION ALL SELECT N'certificate', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.certificates
                UNION ALL SELECT N'asymmetric key', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.asymmetric_keys
                UNION ALL SELECT N'symmetric key', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.symmetric_keys
                UNION ALL SELECT N'database scoped credential', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.database_scoped_credentials
                UNION ALL SELECT N'full-text catalog', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.fulltext_catalogs
                UNION ALL SELECT N'full-text stoplist', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.fulltext_stoplists
                UNION ALL SELECT N'search property list', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.registered_search_property_lists
                UNION ALL SELECT N'message type', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.service_message_types
                UNION ALL SELECT N'contract', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.service_contracts
                UNION ALL SELECT N'service', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.services
                UNION ALL SELECT N'route', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.routes
                UNION ALL SELECT N'remote service binding', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.remote_service_bindings
                UNION ALL SELECT N'event notification', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.event_notifications
                UNION ALL SELECT N'external language', language COLLATE DATABASE_DEFAULT, principal_id FROM sys.external_languages
                UNION ALL SELECT N'external library', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.external_libraries
                UNION ALL SELECT N'external model', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.external_models
            ) AS owned WHERE owned.owner = @uid;
        END;

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

            // 1. Inspect everything before changing anything (no database, login or user is
            //    created or altered if bootstrap is going to refuse).
            var state = await LoginStateAsync(connection, db);
            if (state is { Type: not "S" } nonSql)
            {
                return await RefuseAsync($"'{db.User}' exists but is not a SQL Server login (type {nonSql.Type}). Nothing was changed.");
            }

            if (state is { Disabled: true })
            {
                return await RefuseAsync($"the existing login '{db.User}' is disabled; Goldmark does not re-enable logins. Nothing was changed.");
            }

            if (state is { PolicyChecked: false })
            {
                return await RefuseAsync(
                    $"the existing login '{db.User}' does not enforce SQL Server's password policy (CHECK_POLICY = OFF). " +
                    "Nothing was changed. Goldmark's runtime login uses CHECK_POLICY = ON.");
            }

            if (state is { } lifecycle && (lifecycle.ExpirationChecked || lifecycle.Expired || lifecycle.Locked || lifecycle.MustChange))
            {
                return await RefuseAsync(
                    $"the existing login '{db.User}' can stop working on its own (expiration policy {lifecycle.ExpirationChecked}, " +
                    $"expired {lifecycle.Expired}, locked {lifecycle.Locked}, must change password {lifecycle.MustChange}). " +
                    "Nothing was changed. Goldmark's runtime login uses CHECK_EXPIRATION = OFF.");
            }

            var databaseExists = await ScalarAsync(connection, DatabaseExistsSql, db) == 1;
            if (databaseExists)
            {
                await connection.ChangeDatabaseAsync(db.Name);
            }

            var findings = await FindingsAsync(connection, InspectSql, db);
            await connection.ChangeDatabaseAsync("master");
            if (findings.Count > 0)
            {
                return await RefuseAsync(
                    $"the login '{db.User}' or its environment has rights beyond CONNECT: {string.Join("; ", findings.Distinct())}. " +
                    "Nothing was changed. Remove those rights, or configure a different login.");
            }

            if (state is { PasswordMatches: false } && !bootstrap.RotatePassword)
            {
                return await RefuseAsync(
                    $"the existing login '{db.User}' has a different password. Nothing was changed. " +
                    "To rotate it, run once with GOLDMARK_DB_ROTATE_PASSWORD=true (Goldmark:Bootstrap:RotatePassword).");
            }

            // 2. Provision. The login comes first: SQL Server's password policy can still reject
            //    the password, and that must not leave an empty database behind.
            if (state is null)
            {
                await ExecuteAsync(connection, CreateLoginSql, db);
                Report("created login");
            }
            else if (!state.Value.PasswordMatches)
            {
                await ExecuteAsync(connection, RotatePasswordSql, db);
                Report("rotated login password");
            }

            if (!databaseExists)
            {
                await ScalarAsync(connection, EnsureDatabaseSql, db);
                Report("created database");
            }

            if (state is null)
            {
                await ExecuteAsync(connection, SetDefaultDatabaseSql, db);
            }

            await connection.ChangeDatabaseAsync(db.Name);
            switch (await ScalarAsync(connection, EnsureUserSql, db))
            {
                case 1:
                    Report("created database user");
                    break;
                case 2:
                    return await RefuseAsync(
                        $"database user '{db.User}' exists but belongs to a different login (e.g. a restored database). " +
                        "Remap it with ALTER USER ... WITH LOGIN, or drop it.");
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

    private readonly record struct LoginState(
        string Type, bool Disabled, bool PasswordMatches, bool ExpirationChecked, bool PolicyChecked,
        bool Expired, bool Locked, bool MustChange);

    private static async Task<LoginState?> LoginStateAsync(SqlConnection connection, DatabaseOptions db)
    {
        await using var command = Command(connection, LoginStateSql, db);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new LoginState(reader.GetString(0).Trim(), reader.GetInt32(1) == 1, reader.GetInt32(2) == 1,
            reader.GetInt32(3) == 1, reader.GetInt32(4) == 1, reader.GetInt32(5) == 1, reader.GetInt32(6) == 1,
            reader.GetInt32(7) == 1);
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

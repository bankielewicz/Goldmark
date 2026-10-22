using Goldmark.Api.Audit;
using Goldmark.Api.Configuration;
using Goldmark.Api.Data;
using Microsoft.Data.SqlClient;

namespace Goldmark.Api.Commands;

/// <summary>
/// <c>rotate-db-credentials</c> (DEC-034 3A, DEC-043 item 7): new generated passwords for every SQL login
/// Goldmark generated and keeps in a credential file (the runtime login, the migration login, the bundled SQL
/// Server's administrator), in one run. Each file is written before its login changes, so an interrupted run is
/// finished by the next one, or by <c>bootstrap-db</c>, which applies the file. Audited as System. The
/// application reads its file only at start, so it must be restarted afterwards (the Windows wizard does it).
/// Under Windows authentication, with the administrator as migrator, there is nothing to rotate: the service
/// has no password (a gMSA's rotates itself) and no migration password exists. Exit codes as bootstrap-db:
/// 0 ok, 1 SQL error, 2 invalid configuration, 3 refused (a login Goldmark doesn't manage).
/// </summary>
internal static class RotateCredentialsCommand
{
    public const string Name = "rotate-db-credentials";
    private const int Refused = 3;

    // Read-only, in master: whether the login is a SQL login (type S) and, in the Goldmark database, carries
    // Goldmark's marker for this purpose. Only those are ever changed.
    private const string ManagedLoginSql = """
        SET NOCOUNT ON;
        DECLARE @sql nvarchar(max) = N'SELECT @marker_found = CAST(e.value AS nvarchar(64)) FROM ' + QUOTENAME(@db)
            + N'.sys.database_principals AS u JOIN ' + QUOTENAME(@db) + N'.sys.extended_properties AS e'
            + N' ON e.class = 4 AND e.major_id = u.principal_id AND e.name = N''goldmark:managed'' WHERE u.sid = SUSER_SID(@login);';
        DECLARE @marker_found nvarchar(64);
        EXEC sys.sp_executesql @sql, N'@login sysname, @marker_found nvarchar(64) OUTPUT', @login = @login, @marker_found = @marker_found OUTPUT;
        SELECT CASE
            WHEN NOT EXISTS (SELECT 1 FROM sys.sql_logins WHERE name = @login) THEN N'not a SQL login'
            WHEN @marker IS NULL THEN CASE WHEN @login LIKE N'goldmark[_]admin[_]%' AND IS_SRVROLEMEMBER(N'sysadmin', @login) = 1
                THEN N'ok' ELSE N'not the generated administrator' END
            WHEN ISNULL(@marker_found, N'') <> @marker THEN N'not marked as Goldmark''s'
            ELSE N'ok' END;
        """;

    private const string RotateSql = """
        SET NOCOUNT ON;
        DECLARE @sql nvarchar(max) = N'ALTER LOGIN ' + QUOTENAME(@login) + N' WITH PASSWORD = ' + QUOTENAME(@password, N'''') + N';';
        EXEC sys.sp_executesql @sql;
        """;

    private const string AuditSql = """
        SET NOCOUNT ON;
        DECLARE @sql nvarchar(max) = N'INSERT INTO ' + QUOTENAME(@db) + N'.cmdb.AuditLog (ActorType, Action, Details) '
            + N'VALUES (''System'', @action, JSON_OBJECT(''logins'': JSON_QUERY(@logins), ''via'': N''rotate-db-credentials''));';
        EXEC sys.sp_executesql @sql, N'@action nvarchar(64), @logins nvarchar(max)', @action = @action, @logins = @logins;
        """;

    private sealed record Target(string Purpose, string Setting, string File, string Login, string? Marker);

    public static async Task<int> RunAsync(IConfiguration configuration, TextWriter output, TextWriter error)
    {
        var errors = ConfigurationPreflight.BindingErrors(configuration);
        var db = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
        var migration = configuration.GetSection(MigrationOptions.SectionName).Get<MigrationOptions>() ?? new MigrationOptions();
        var bootstrap = configuration.GetSection(BootstrapOptions.SectionName).Get<BootstrapOptions>() ?? new BootstrapOptions();
        var service = configuration.GetSection(ServiceOptions.SectionName).Get<ServiceOptions>() ?? new ServiceOptions();
        var targets = new List<Target>();
        LoginCredential? generatedAdmin = null;
        if (errors.Count == 0)
        {
            if (!db.UsesWindowsAuthentication)
            {
                AddTarget(targets, errors, "runtime", DatabaseOptions.SectionName, db.CredentialFile, db.Password, BootstrapDatabaseCommand.RuntimeMarkerValue);
            }

            if (!migration.UsesAdministrator)
            {
                AddTarget(targets, errors, "migration", MigrationOptions.SectionName, migration.CredentialFile, migration.Password, BootstrapDatabaseCommand.MigrationMarkerValue);
            }

            if (!string.IsNullOrWhiteSpace(bootstrap.AdminCredentialFile) && CredentialFile.Exists(bootstrap.AdminCredentialFile))
            {
                if (CredentialFile.TryRead(bootstrap.AdminCredentialFile, out var admin, out var problem))
                {
                    generatedAdmin = admin;
                    targets.Add(new Target("administrator", BootstrapOptions.SectionName, bootstrap.AdminCredentialFile, admin.User, null));
                }
                else
                {
                    errors.Add($"{BootstrapOptions.SectionName}:AdminCredentialFile {problem}.");
                }
            }

            errors.AddRange(ConnectionErrors(db, bootstrap, generatedAdmin));
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

        if (targets.Count == 0)
        {
            await output.WriteLineAsync($"{Name}: nothing to rotate: Goldmark keeps no SQL login password here " +
                "(Windows authentication has none, a gMSA's rotates itself, and migrations run as the administrator).");
            return 0;
        }

        var builder = bootstrap.AdminUsesWindows
            ? SqlConnectionFactory.BuildIntegrated(db, database: "master")
            : SqlConnectionFactory.Build(db, generatedAdmin?.User ?? bootstrap.AdminUser, generatedAdmin?.Password ?? bootstrap.AdminPassword, database: "master");
        builder.Pooling = false;
        try
        {
            await using var connection = await CommandSql.OpenWithRetryAsync(builder.ConnectionString, TimeSpan.FromSeconds(bootstrap.WaitSeconds), output, Name);
            if (await ScalarAsync(connection, BootstrapDatabaseCommand.AdminRightsSql) != "1")
            {
                return await RefuseAsync(error, "the administrator needs CONTROL SERVER (for example sysadmin). Nothing was changed.");
            }

            // 1. Read-only: every login must be one Goldmark manages, before anything changes.
            foreach (var target in targets)
            {
                var state = await ScalarAsync(connection, ManagedLoginSql, ("@db", db.Name), ("@login", target.Login), ("@marker", target.Marker));
                if (state != "ok")
                {
                    return await RefuseAsync(error, $"the {target.Purpose} login '{target.Login}' from {target.Setting}:CredentialFile is {state}. Nothing was changed.");
                }
            }

            // 2. Each file before its login: an interrupted run leaves a file that bootstrap-db (signed in as the
            //    administrator) applies. The administrator's own file can't be repaired that way, since signing
            //    in needs it: its new file waits beside the old one until the login has changed.
            foreach (var target in targets)
            {
                var credential = new LoginCredential(target.Login, CredentialFile.GeneratePassword());
                var administrator = target.Purpose == "administrator";
                var file = administrator ? target.File + ".new" : target.File;
                var protection = CredentialFile.Write(file, credential, target.Purpose == "runtime" ? service.Account : null);
                await ExecuteAsync(connection, RotateSql, ("@login", target.Login), ("@password", credential.Password));
                if (administrator)
                {
                    File.Move(file, target.File, overwrite: true);
                }

                await output.WriteLineAsync($"{Name}: {target.Purpose} login '{target.Login}': new password, written to {target.File}" +
                    BootstrapDatabaseCommand.Describe(protection, target.Purpose == "runtime" ? service.Account : null));
            }

            var logins = System.Text.Json.JsonSerializer.Serialize(targets.Select(t => new { purpose = t.Purpose, login = t.Login }));
            await ExecuteAsync(connection, AuditSql, ("@db", db.Name), ("@action", AuditActions.CredentialsRotated), ("@logins", logins));
            await output.WriteLineAsync($"{Name}: done. Restart Goldmark now, so it reads its new password " +
                "(Docker: docker compose restart app; Windows: Restart-Service Goldmark).");
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await error.WriteLineAsync($"{Name}: could not write a credential file ({ex.GetType().Name}): {ex.Message} " +
                "The logins listed above were changed; run this again once the folder is writable.");
            return 1;
        }
    }

    /// <summary>A login Goldmark generated: its file is configured and no password is set in settings.</summary>
    private static void AddTarget(List<Target> targets, List<string> errors, string purpose, string section, string? file, string? password, string marker)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            if (!string.IsNullOrEmpty(password))
            {
                errors.Add($"{section}:Password is set in settings: Goldmark doesn't keep it, so change it there and run db-init.");
            }

            return;
        }

        if (!string.IsNullOrEmpty(password))
        {
            errors.Add($"{section}:Password is set in settings: remove it (the file is then used) or change it there and run db-init.");
            return;
        }

        if (!CredentialFile.TryRead(file, out var stored, out var problem) && problem != CredentialFile.NotForThisAccount)
        {
            errors.Add($"{section}:CredentialFile {problem}.");
            return;
        }

        targets.Add(new Target(purpose, section, file, stored.User, marker));
    }

    private static IEnumerable<string> ConnectionErrors(DatabaseOptions db, BootstrapOptions bootstrap, LoginCredential? generatedAdmin)
    {
        if (string.IsNullOrWhiteSpace(db.Host))
        {
            yield return $"{DatabaseOptions.SectionName}:Host is required.";
        }

        if (!SqlIdentifier.IsValid(db.Name) || SqlIdentifier.IsSystemDatabase(db.Name))
        {
            yield return $"{DatabaseOptions.SectionName}:Name must be a simple identifier and not a system database.";
        }

        if (!bootstrap.AdminUsesWindows && generatedAdmin is null
            && (string.IsNullOrWhiteSpace(bootstrap.AdminUser) || string.IsNullOrEmpty(bootstrap.AdminPassword)))
        {
            yield return $"{BootstrapOptions.SectionName}:AdminUser and AdminPassword are required, unless {BootstrapOptions.SectionName}:AdminAuthentication " +
                $"is '{DatabaseOptions.WindowsAuthentication}' or the generated administrator's file exists.";
        }

        foreach (var problem in BootstrapDatabaseCommand.CommonConnectionErrors(db))
        {
            yield return problem;
        }
    }

    private static async Task<int> RefuseAsync(TextWriter error, string reason)
    {
        await error.WriteLineAsync($"{Name}: refusing to continue: {reason}");
        return Refused;
    }

    private static SqlCommand Command(SqlConnection connection, string sql, (string Name, string? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 60;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.Add(new SqlParameter(name, System.Data.SqlDbType.NVarChar, -1) { Value = (object?)value ?? DBNull.Value });
        }

        return command;
    }

    private static async Task<string?> ScalarAsync(SqlConnection connection, string sql, params (string Name, string? Value)[] parameters)
    {
        await using var command = Command(connection, sql, parameters);
        return Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql, params (string Name, string? Value)[] parameters)
    {
        await using var command = Command(connection, sql, parameters);
        await command.ExecuteNonQueryAsync();
    }
}

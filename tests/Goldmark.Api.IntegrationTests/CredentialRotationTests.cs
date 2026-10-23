using System.Text.Json;
using Goldmark.Api.IntegrationTests.Infrastructure;

namespace Goldmark.Api.IntegrationTests;

/// <summary>
/// <c>rotate-db-credentials</c> (DEC-034 3A, DEC-043 item 7): every generated SQL login gets a new password in
/// one run, its file first; the old passwords stop working, the new ones work, the run is audited as System, and
/// a login Goldmark doesn't manage is refused with nothing changed.
/// </summary>
[Collection(SqlServerTests.Name)]
public sealed class CredentialRotationTests(SqlServerFixture sql)
{
    private sealed record Files(string Runtime, string Migration);

    private Files NewFiles()
    {
        var directory = sql.NewDirectory();
        return new Files(Path.Combine(directory, "db-runtime", "credential.json"), Path.Combine(directory, "db-admin", "migration.json"));
    }

    private static Dictionary<string, string?> FromFiles(Files files) => new()
    {
        ["Goldmark:Database:User"] = "",
        ["Goldmark:Database:Password"] = "",
        ["Goldmark:Database:CredentialFile"] = files.Runtime,
        ["Goldmark:Migration:User"] = "",
        ["Goldmark:Migration:Password"] = "",
        ["Goldmark:Migration:CredentialFile"] = files.Migration,
    };

    private static (string User, string Password) Read(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return (document.RootElement.GetProperty("user").GetString()!, document.RootElement.GetProperty("password").GetString()!);
    }

    private Task<int> PasswordMatchesAsync(string login, string password) =>
        sql.AdminScalarAsync<int>("master", $"SELECT PWDCOMPARE(N'{password}', password_hash) FROM sys.sql_logins WHERE name = N'{login}';");

    [Fact]
    public async Task A_rotation_while_another_credential_writer_runs_is_refused_with_nothing_changed()
    {
        // Codex on #39: two interleaved runs could leave a file and its login with different passwords.
        var installation = sql.NewInstallation();
        var files = NewFiles();
        var provisioned = await sql.RunAsync(installation, ["bootstrap-db", "migrate"], FromFiles(files));
        Assert.True(provisioned.Code == 0, provisioned.ToString());
        var before = (Read(files.Runtime), Read(files.Migration));
        await using var running = await HoldCredentialLockAsync();

        var refused = await sql.RunAsync(installation, ["rotate-db-credentials"], FromFiles(files));

        Assert.Equal(3, refused.Code);
        Assert.Contains("another bootstrap-db or rotate-db-credentials run is changing Goldmark's logins", refused.Error, StringComparison.Ordinal);
        Assert.Equal(before, (Read(files.Runtime), Read(files.Migration)));
        Assert.Equal(1, await PasswordMatchesAsync(before.Item1.User, before.Item1.Password));
    }

    [Fact]
    public async Task Bootstrap_db_while_another_credential_writer_runs_is_refused_before_creating_anything()
    {
        // Codex on #39 (round 2): bootstrap-db writes the same files and logins, so it takes the same lock.
        var installation = sql.NewInstallation();
        await using var running = await HoldCredentialLockAsync();

        var refused = await sql.RunAsync(installation, ["bootstrap-db"], FromFiles(NewFiles()));

        Assert.Equal(3, refused.Code);
        Assert.Contains("another bootstrap-db or rotate-db-credentials run is changing Goldmark's logins", refused.Error, StringComparison.Ordinal);
        Assert.Equal(0, await sql.AdminScalarAsync<int>("master", $"SELECT COUNT(*) FROM sys.databases WHERE name = N'{installation.Database}';"));
        Assert.Equal(0, await sql.AdminScalarAsync<int>("master", $"SELECT COUNT(*) FROM sys.server_principals WHERE name = N'{installation.RuntimeUser}';"));
    }

    /// <summary>The lock both commands take, held by another session until disposed.</summary>
    private async Task<Microsoft.Data.SqlClient.SqlConnection> HoldCredentialLockAsync()
    {
        var running = await sql.OpenAsync(SqlServerFixture.AdminUser, sql.AdminPassword, "master");
        await using var take = running.CreateCommand();
        take.CommandText = "EXEC sys.sp_getapplock @Resource = N'goldmark:credentials', @LockMode = N'Exclusive', @LockOwner = N'Session';";
        await take.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return running;
    }

    [Fact]
    public async Task Both_generated_logins_get_new_passwords_their_files_first_and_the_run_is_audited()
    {
        var installation = sql.NewInstallation();
        var files = NewFiles();
        var provisioned = await sql.RunAsync(installation, ["bootstrap-db", "migrate"], FromFiles(files));
        Assert.True(provisioned.Code == 0, provisioned.ToString());
        var (runtime, oldRuntime) = Read(files.Runtime);
        var (migrator, oldMigrator) = Read(files.Migration);

        var rotated = await sql.RunAsync(installation, ["rotate-db-credentials"], FromFiles(files));

        Assert.True(rotated.Code == 0, rotated.ToString());
        var (sameRuntime, newRuntime) = Read(files.Runtime);
        var (sameMigrator, newMigrator) = Read(files.Migration);
        Assert.Equal((runtime, migrator), (sameRuntime, sameMigrator));
        Assert.NotEqual(oldRuntime, newRuntime);
        Assert.NotEqual(oldMigrator, newMigrator);
        Assert.Equal(1, await PasswordMatchesAsync(runtime, newRuntime));
        Assert.Equal(0, await PasswordMatchesAsync(runtime, oldRuntime));
        Assert.Equal(1, await PasswordMatchesAsync(migrator, newMigrator));
        Assert.Equal(0, await PasswordMatchesAsync(migrator, oldMigrator));
        Assert.Contains("Restart Goldmark now", rotated.Output, StringComparison.Ordinal);
        foreach (var secret in new[] { oldRuntime, newRuntime, oldMigrator, newMigrator })
        {
            Assert.DoesNotContain(secret, rotated.Output + rotated.Error, StringComparison.Ordinal);
        }

        Assert.Equal($"System|[{{\"purpose\":\"runtime\",\"login\":\"{runtime}\"}},{{\"purpose\":\"migration\",\"login\":\"{migrator}\"}}]",
            await sql.AdminScalarAsync<string>(installation.Database, """
                SELECT ActorType + N'|' + JSON_QUERY(CAST(Details AS nvarchar(max)), '$.logins') FROM cmdb.AuditLog WHERE Action = N'database.credentials.rotated';
                """));

        // The application and db-init go on with the new files.
        var again = await sql.RunAsync(installation, ["bootstrap-db", "migrate"], FromFiles(files));
        Assert.True(again.Code == 0, again.ToString());
        Assert.DoesNotContain("changed login password", again.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_file_written_before_an_interrupted_rotation_is_applied_by_the_next_db_init()
    {
        var installation = sql.NewInstallation();
        var files = NewFiles();
        Assert.Equal(0, (await sql.RunAsync(installation, ["bootstrap-db", "migrate"], FromFiles(files))).Code);
        var (runtime, _) = Read(files.Runtime);
        // As if rotate-db-credentials stopped after writing the file, before changing the login.
        var interrupted = "Interrupted-Pa55word-4b-0000000x";
        File.WriteAllText(files.Runtime, JsonSerializer.Serialize(new { user = runtime, password = interrupted }));

        var repaired = await sql.RunAsync(installation, ["bootstrap-db", "migrate"], FromFiles(files));

        Assert.True(repaired.Code == 0, repaired.ToString());
        Assert.Equal(1, await PasswordMatchesAsync(runtime, interrupted));
    }

    [Fact]
    public async Task A_login_Goldmark_does_not_manage_is_refused_and_nothing_changes()
    {
        var installation = sql.NewInstallation();
        var files = NewFiles();
        Assert.Equal(0, (await sql.RunAsync(installation, ["bootstrap-db", "migrate"], FromFiles(files))).Code);
        var (runtime, runtimePassword) = Read(files.Runtime);
        var (migrator, migratorPassword) = Read(files.Migration);
        await sql.AsAdminAsync(installation.Database, $"EXEC sys.sp_dropextendedproperty @name = N'goldmark:managed', @level0type = N'USER', @level0name = N'{migrator}';");

        var refused = await sql.RunAsync(installation, ["rotate-db-credentials"], FromFiles(files));

        Assert.Equal(3, refused.Code);
        Assert.Contains($"the migration login '{migrator}' from Goldmark:Migration:CredentialFile is not marked as Goldmark's", refused.Error, StringComparison.Ordinal);
        Assert.Equal((runtime, runtimePassword), Read(files.Runtime));
        Assert.Equal((migrator, migratorPassword), Read(files.Migration));
        Assert.Equal(1, await PasswordMatchesAsync(runtime, runtimePassword));
    }

    [Fact]
    public async Task Passwords_given_in_settings_are_not_rotated_here()
    {
        var installation = sql.NewInstallation();

        var refused = await sql.RunAsync(installation, ["rotate-db-credentials"]);

        Assert.Equal(2, refused.Code);
        Assert.Contains("Goldmark:Database:Password is set in settings: Goldmark doesn't keep it", refused.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(installation.RuntimePassword, refused.Error, StringComparison.Ordinal);
    }
}

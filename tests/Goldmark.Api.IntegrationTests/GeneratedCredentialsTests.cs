using System.Text.Json;
using Goldmark.Api.IntegrationTests.Infrastructure;

namespace Goldmark.Api.IntegrationTests;

/// <summary>
/// Generated runtime and migration logins kept in credential files (slice 2c, DEC-034): generated
/// when nothing is configured, kept on later runs, recovered by their marker when a file is lost
/// or when upgrading from passwords in settings (owner decision 1A), and written from the
/// settings when those are given (Mode Existing, owner decision 5A).
/// </summary>
[Collection(SqlServerTests.Name)]
public sealed class GeneratedCredentialsTests(SqlServerFixture sql)
{
    private sealed record Files(string Runtime, string Migration);

    private Files NewFiles()
    {
        var directory = sql.NewDirectory();
        return new Files(Path.Combine(directory, "db-runtime", "credential.json"), Path.Combine(directory, "db-admin", "migration.json"));
    }

    /// <summary>Both logins from credential files, with no names or passwords in the settings.</summary>
    private static Dictionary<string, string?> FromFiles(Files files, Dictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Goldmark:Database:User"] = "",
            ["Goldmark:Database:Password"] = "",
            ["Goldmark:Database:CredentialFile"] = files.Runtime,
            ["Goldmark:Migration:User"] = "",
            ["Goldmark:Migration:Password"] = "",
            ["Goldmark:Migration:CredentialFile"] = files.Migration,
        };
        foreach (var (key, value) in extra ?? [])
        {
            settings[key] = value;
        }

        return settings;
    }

    private static (string User, string Password) Read(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return (document.RootElement.GetProperty("user").GetString()!, document.RootElement.GetProperty("password").GetString()!);
    }

    private Task<int> PasswordMatchesAsync(string login, string password) =>
        sql.AdminScalarAsync<int>("master", $"SELECT PWDCOMPARE(N'{password}', password_hash) FROM sys.sql_logins WHERE name = N'{login}';");

    private static void AssertNoSecret(CommandResult result, params string[] secrets)
    {
        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(secret, result.Output, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, result.Error, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Without_settings_both_logins_are_generated_and_kept_in_private_files() // owner decision 2A
    {
        var installation = sql.NewInstallation();
        var files = NewFiles();

        var result = await sql.RunAsync(installation, ["bootstrap-db", "migrate"], FromFiles(files));

        Assert.True(result.Code == 0, result.ToString());
        var (runtimeUser, runtimePassword) = Read(files.Runtime);
        var (migrationUser, migrationPassword) = Read(files.Migration);
        Assert.Matches("^goldmark_app_[a-z0-9]{10}$", runtimeUser);
        Assert.Matches("^goldmark_migrator_[a-z0-9]{10}$", migrationUser);
        Assert.Equal(32, runtimePassword.Length);
        AssertNoSecret(result, runtimePassword, migrationPassword);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(files.Runtime));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Path.GetDirectoryName(files.Migration)!));
        }

        Assert.Equal(1, await sql.ScalarAsync<int>(runtimeUser, runtimePassword, installation.Database, "SELECT IS_ROLEMEMBER('goldmark_runtime');"));
        Assert.Equal(1, await sql.ScalarAsync<int>(migrationUser, migrationPassword, installation.Database, "SELECT IS_ROLEMEMBER('db_owner');"));

        var before = (File.ReadAllText(files.Runtime), File.ReadAllText(files.Migration));
        var again = await sql.RunAsync(installation, ["bootstrap-db", "migrate"], FromFiles(files));

        Assert.True(again.Code == 0, again.ToString());
        Assert.Equal(before, (File.ReadAllText(files.Runtime), File.ReadAllText(files.Migration)));
        Assert.DoesNotContain("changed login password", again.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("credential file", again.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stack_with_passwords_in_settings_keeps_its_logins_and_gets_generated_passwords() // owner decision 1A
    {
        var (installation, _) = await sql.ProvisionAsync(); // as before slice 2c: names and passwords in settings
        var files = NewFiles();

        var upgraded = await sql.RunAsync(installation, ["bootstrap-db", "migrate"], FromFiles(files));

        Assert.True(upgraded.Code == 0, upgraded.ToString());
        Assert.Contains($"runtime login: its credential file is missing: keeping the existing login '{installation.RuntimeUser}' with a new generated password",
            upgraded.Output, StringComparison.Ordinal);
        var (runtimeUser, runtimePassword) = Read(files.Runtime);
        var (migrationUser, migrationPassword) = Read(files.Migration);
        Assert.Equal((installation.RuntimeUser, installation.MigrationUser), (runtimeUser, migrationUser));
        Assert.Equal(0, await PasswordMatchesAsync(runtimeUser, installation.RuntimePassword));
        Assert.Equal(1, await PasswordMatchesAsync(runtimeUser, runtimePassword));
        Assert.Equal(0, await PasswordMatchesAsync(migrationUser, installation.MigrationPassword));
        Assert.Equal(1, await PasswordMatchesAsync(migrationUser, migrationPassword));
        AssertNoSecret(upgraded, runtimePassword, migrationPassword);
    }

    [Fact]
    public async Task A_lost_credential_file_gives_the_same_login_a_new_password()
    {
        var installation = sql.NewInstallation();
        var files = NewFiles();
        Assert.Equal(0, (await sql.RunAsync(installation, ["bootstrap-db", "migrate"], FromFiles(files))).Code);
        var (user, oldPassword) = Read(files.Runtime);

        File.Delete(files.Runtime);
        var recovered = await sql.RunAsync(installation, ["bootstrap-db", "migrate"], FromFiles(files));

        Assert.True(recovered.Code == 0, recovered.ToString());
        var (sameUser, newPassword) = Read(files.Runtime);
        Assert.Equal(user, sameUser);
        Assert.NotEqual(oldPassword, newPassword);
        Assert.Equal(0, await PasswordMatchesAsync(user, oldPassword));
        Assert.Equal(1, await sql.ScalarAsync<int>(user, newPassword, installation.Database, "SELECT IS_ROLEMEMBER('goldmark_runtime');"));
    }

    [Fact]
    public async Task Settings_given_with_a_credential_file_are_written_to_it_and_later_runs_need_only_the_file()
    {
        var installation = sql.NewInstallation();
        var files = NewFiles();
        var withSettings = new Dictionary<string, string?>
        {
            ["Goldmark:Database:CredentialFile"] = files.Runtime,
            ["Goldmark:Migration:CredentialFile"] = files.Migration,
        };

        var seeded = await sql.RunAsync(installation, ["bootstrap-db", "migrate"], withSettings);

        Assert.True(seeded.Code == 0, seeded.ToString());
        Assert.Equal((installation.RuntimeUser, installation.RuntimePassword), Read(files.Runtime));
        Assert.Equal((installation.MigrationUser, installation.MigrationPassword), Read(files.Migration));

        var fileOnly = await sql.RunAsync(installation, ["bootstrap-db", "migrate"], FromFiles(files));
        Assert.True(fileOnly.Code == 0, fileOnly.ToString());
        Assert.DoesNotContain("changed login password", fileOnly.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mode_Existing_writes_the_DBAs_logins_to_the_credential_files() // owner decision 5A
    {
        var i = sql.NewInstallation();
        await sql.AsAdminAsync("master", $"""
            CREATE DATABASE {i.Database};
            CREATE LOGIN {i.RuntimeUser} WITH PASSWORD = N'{i.RuntimePassword}', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
            CREATE LOGIN {i.MigrationUser} WITH PASSWORD = N'{i.MigrationPassword}', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
            EXEC {i.Database}.sys.sp_executesql N'CREATE USER {i.RuntimeUser} FOR LOGIN {i.RuntimeUser};
                CREATE USER {i.MigrationUser} FOR LOGIN {i.MigrationUser};
                ALTER ROLE db_owner ADD MEMBER {i.MigrationUser};';
            """);
        var files = NewFiles();

        var result = await sql.RunAsync(i, ["bootstrap-db", "migrate"], new Dictionary<string, string?>
        {
            ["Goldmark:Bootstrap:Mode"] = "Existing",
            ["Goldmark:Database:CredentialFile"] = files.Runtime,
            ["Goldmark:Migration:CredentialFile"] = files.Migration,
        });

        Assert.True(result.Code == 0, result.ToString());
        Assert.Equal((i.RuntimeUser, i.RuntimePassword), Read(files.Runtime));
        Assert.Equal((i.MigrationUser, i.MigrationPassword), Read(files.Migration));
        Assert.DoesNotContain("changed login password", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Migrate_refuses_settings_that_differ_from_the_credential_file()
    {
        var installation = sql.NewInstallation();
        var files = NewFiles();
        Assert.Equal(0, (await sql.RunAsync(installation, ["bootstrap-db", "migrate"], FromFiles(files))).Code);

        var refused = await sql.RunAsync(installation, ["migrate"], FromFiles(files, new() { ["Goldmark:Database:Password"] = SqlServerFixture.Secret() }));

        Assert.Equal(2, refused.Code);
        Assert.Contains("Goldmark:Database:User and Password differ from Goldmark:Database:CredentialFile", refused.Error, StringComparison.Ordinal);
    }
}

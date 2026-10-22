using System.Diagnostics;
using System.Text.Json;
using Goldmark.Api.IntegrationTests.Infrastructure;

namespace Goldmark.Api.IntegrationTests;

/// <summary>A SQL Server container of its own: these tests disable sa (and re-enable it as the recovery does).</summary>
[CollectionDefinition(Name)]
public sealed class SaDisabledTests : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SQL Server 2025, generated administrator";
}

/// <summary>
/// The bundled SQL Server's generated administrator (slice 2c, DEC-034): the first run signs in as
/// sa, creates a sysadmin login with a generated name and password, writes it to its credential
/// file and disables sa; later runs sign in with that file and never as sa. Losing the file is
/// recovered by re-enabling sa (measured: <c>mssql-conf set-sa-password</c> does that), after
/// which the next run resets the same administrator and disables sa again.
/// </summary>
[Collection(SaDisabledTests.Name)]
public sealed class GeneratedAdministratorTests(SqlServerFixture sql)
{
    private sealed record Files(string Runtime, string Migration, string Admin);

    private Files NewFiles()
    {
        var directory = sql.NewDirectory();
        return new Files(Path.Combine(directory, "db-runtime", "credential.json"), Path.Combine(directory, "db-admin", "migration.json"),
            Path.Combine(directory, "db-admin", "admin.json"));
    }

    /// <summary>The settings the bundled Compose file gives db-init.</summary>
    private Dictionary<string, string?> Bundled(Files files) => new()
    {
        ["Goldmark:Database:User"] = "",
        ["Goldmark:Database:Password"] = "",
        ["Goldmark:Database:CredentialFile"] = files.Runtime,
        ["Goldmark:Migration:User"] = "",
        ["Goldmark:Migration:Password"] = "",
        ["Goldmark:Migration:CredentialFile"] = files.Migration,
        ["Goldmark:Bootstrap:AdminUser"] = "sa",
        ["Goldmark:Bootstrap:AdminPassword"] = sql.SaPassword,
        ["Goldmark:Bootstrap:AdminCredentialFile"] = files.Admin,
    };

    private static (string User, string Password) Read(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return (document.RootElement.GetProperty("user").GetString()!, document.RootElement.GetProperty("password").GetString()!);
    }

    private Task<int> SaDisabledAsync() => sql.AdminScalarAsync<int>("master", "SELECT CAST(is_disabled AS int) FROM sys.server_principals WHERE sid = 0x01;");

    /// <summary>A first run, as on a fresh bundled stack: sa is enabled until it finishes.</summary>
    private async Task<(Installation Installation, Files Files, CommandResult Result)> FirstRunAsync()
    {
        await sql.EnableSaAsync();
        var installation = sql.NewInstallation();
        var files = NewFiles();
        var result = await sql.RunAsync(installation, ["bootstrap-db", "migrate"], Bundled(files));
        Assert.True(result.Code == 0, result.ToString());
        return (installation, files, result);
    }

    [Fact]
    public async Task The_first_run_creates_Goldmarks_administrator_and_disables_sa()
    {
        var (installation, files, result) = await FirstRunAsync();

        var (admin, adminPassword) = Read(files.Admin);
        Assert.Matches("^goldmark_admin_[a-z0-9]{10}$", admin);
        Assert.Contains($"bootstrap-db: created the generated administrator '{admin}' (sysadmin)", result.Output, StringComparison.Ordinal);
        Assert.Contains("bootstrap-db: disabled sa", result.Output, StringComparison.Ordinal);
        Assert.Equal(1, await SaDisabledAsync());
        Assert.Equal(1, await sql.AdminScalarAsync<int>("master", $"SELECT IS_SRVROLEMEMBER(N'sysadmin', N'{admin}');"));
        Assert.Equal(1, await sql.AdminScalarAsync<int>("master", $"SELECT CASE WHEN owner_sid = 0x01 THEN 1 ELSE 0 END FROM sys.databases WHERE name = N'{installation.Database}';"));
        Assert.Equal(1, await sql.AdminScalarAsync<int>(installation.Database,
            $"SELECT CASE WHEN CAST(value AS varbinary(85)) = SUSER_SID(N'{admin}') THEN 1 ELSE 0 END FROM sys.extended_properties WHERE class = 0 AND name = N'goldmark:admin-login';"));
        foreach (var secret in new[] { adminPassword, Read(files.Runtime).Password, Read(files.Migration).Password })
        {
            Assert.DoesNotContain(secret, result.Output + result.Error, StringComparison.Ordinal);
        }

        var again = await sql.RunAsync(installation, ["bootstrap-db", "migrate"], Bundled(files));

        Assert.True(again.Code == 0, again.ToString());
        Assert.Contains($"bootstrap-db: signed in as the generated administrator '{admin}'", again.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("disabled sa", again.Output, StringComparison.Ordinal);
        Assert.Equal((admin, adminPassword), Read(files.Admin));
    }

    [Fact]
    public async Task Rotation_gives_the_generated_administrator_a_new_password_and_later_runs_sign_in_with_it() // DEC-043 item 7
    {
        var (installation, files, _) = await FirstRunAsync();
        var (admin, oldPassword) = Read(files.Admin);

        var rotated = await sql.RunAsync(installation, ["rotate-db-credentials"], Bundled(files));

        Assert.True(rotated.Code == 0, rotated.ToString());
        var (sameAdmin, newPassword) = Read(files.Admin);
        Assert.Equal(admin, sameAdmin);
        Assert.NotEqual(oldPassword, newPassword);
        Assert.False(File.Exists(files.Admin + ".new"));
        Assert.Contains($"rotate-db-credentials: administrator login '{admin}': new password", rotated.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(newPassword, rotated.Output + rotated.Error, StringComparison.Ordinal);
        Assert.Equal(1, await SaDisabledAsync());

        var again = await sql.RunAsync(installation, ["bootstrap-db", "migrate"], Bundled(files));
        Assert.True(again.Code == 0, again.ToString());
        Assert.Contains($"bootstrap-db: signed in as the generated administrator '{admin}'", again.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_lost_administrator_file_is_recovered_by_re_enabling_sa()
    {
        var (installation, files, _) = await FirstRunAsync();
        var (admin, oldPassword) = Read(files.Admin);
        File.Delete(files.Admin);

        // sa is disabled and the file is gone: stop at once (a disabled login is not retried) with the recovery steps.
        var watch = Stopwatch.StartNew();
        var locked = await sql.RunAsync(installation, ["bootstrap-db", "migrate"], Bundled(files));
        watch.Stop();

        Assert.Equal(1, locked.Code);
        Assert.Contains("SQL Server error 18470", locked.Error, StringComparison.Ordinal);
        Assert.Contains("follow docs/ops/database-recovery.md: re-enable sa with mssql-conf", locked.Error, StringComparison.Ordinal);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"took {watch.Elapsed}");

        await sql.EnableSaAsync(); // what mssql-conf set-sa-password does
        var recovered = await sql.RunAsync(installation, ["bootstrap-db", "migrate"], Bundled(files));

        Assert.True(recovered.Code == 0, recovered.ToString());
        Assert.Contains($"bootstrap-db: reset the password of the generated administrator '{admin}'", recovered.Output, StringComparison.Ordinal);
        var (sameAdmin, newPassword) = Read(files.Admin);
        Assert.Equal(admin, sameAdmin);
        Assert.Equal(0, await sql.AdminScalarAsync<int>("master", $"SELECT PWDCOMPARE(N'{oldPassword}', password_hash) FROM sys.sql_logins WHERE name = N'{admin}';"));
        Assert.Equal(1, await sql.AdminScalarAsync<int>("master", $"SELECT PWDCOMPARE(N'{newPassword}', password_hash) FROM sys.sql_logins WHERE name = N'{admin}';"));
        Assert.Equal(1, await SaDisabledAsync());
    }

    [Fact]
    public async Task An_administrator_file_that_no_longer_signs_in_names_the_recovery_and_never_tries_sa()
    {
        var (installation, files, _) = await FirstRunAsync();
        var (admin, _) = Read(files.Admin);
        await sql.EnableSaAsync(); // even with sa usable, it is not tried while the file exists
        File.WriteAllText(files.Admin, JsonSerializer.Serialize(new { user = admin, password = SqlServerFixture.Secret() }));

        var refused = await sql.RunAsync(installation, ["bootstrap-db", "migrate"], new Dictionary<string, string?>(Bundled(files))
        {
            ["Goldmark:Bootstrap:WaitSeconds"] = "0",
        });

        Assert.Equal(1, refused.Code);
        Assert.Contains($"the generated administrator in {files.Admin} cannot sign in, and sa is never tried while that file exists", refused.Error, StringComparison.Ordinal);
        Assert.Equal(0, await SaDisabledAsync()); // the run never got far enough to sign in as anyone
    }

    [Fact]
    public async Task A_marker_naming_another_login_never_makes_it_sysadmin()
    {
        var (installation, files, _) = await FirstRunAsync();
        var (admin, _) = Read(files.Admin);
        var victim = $"{installation.RuntimeUser}_victim";
        var victimPassword = SqlServerFixture.Secret();
        await sql.AsAdminAsync("master", $"CREATE LOGIN {victim} WITH PASSWORD = N'{victimPassword}';");
        // What the migration login (db_owner) could do: point the marker at a login it chose.
        await sql.AsAdminAsync(installation.Database,
            $"DECLARE @sid varbinary(85) = SUSER_SID(N'{victim}'); EXEC sys.sp_updateextendedproperty @name = N'goldmark:admin-login', @value = @sid;");
        File.Delete(files.Admin);
        await sql.EnableSaAsync();

        var result = await sql.RunAsync(installation, ["bootstrap-db", "migrate"], Bundled(files));

        Assert.True(result.Code == 0, result.ToString());
        Assert.Contains($"the login '{victim}' named by the goldmark:admin-login marker is not a sysadmin SQL login with Goldmark's prefix", result.Output, StringComparison.Ordinal);
        var (newAdmin, _) = Read(files.Admin);
        Assert.NotEqual(admin, newAdmin);
        Assert.Equal(0, await sql.AdminScalarAsync<int>("master", $"SELECT IS_SRVROLEMEMBER(N'sysadmin', N'{victim}');"));
        Assert.Equal(1, await sql.AdminScalarAsync<int>("master", $"SELECT PWDCOMPARE(N'{victimPassword}', password_hash) FROM sys.sql_logins WHERE name = N'{victim}';"));
        Assert.Equal(1, await SaDisabledAsync());
    }

    [Fact]
    public async Task A_stack_from_before_generated_credentials_keeps_its_logins_and_disables_sa() // owner decision 1A
    {
        await sql.EnableSaAsync();
        var (installation, _) = await sql.ProvisionAsync(); // names and passwords in settings, an administrator of its own
        var files = NewFiles();

        var upgraded = await sql.RunAsync(installation, ["bootstrap-db", "migrate"], Bundled(files));

        Assert.True(upgraded.Code == 0, upgraded.ToString());
        Assert.Equal(installation.RuntimeUser, Read(files.Runtime).User);
        Assert.Equal(installation.MigrationUser, Read(files.Migration).User);
        Assert.Matches("^goldmark_admin_[a-z0-9]{10}$", Read(files.Admin).User);
        Assert.Equal(1, await SaDisabledAsync());
        Assert.Equal(1, await sql.ScalarAsync<int>(installation.RuntimeUser, Read(files.Runtime).Password, installation.Database, "SELECT IS_ROLEMEMBER('goldmark_runtime');"));
    }
}

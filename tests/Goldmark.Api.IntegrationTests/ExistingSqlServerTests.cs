using Goldmark.Api.IntegrationTests.Infrastructure;

namespace Goldmark.Api.IntegrationTests;

/// <summary>
/// Using an existing SQL Server (US-01, DEC-032): a dedicated administrator login instead of
/// <c>sa</c>, and Mode Existing, where a DBA has created the database and both logins and
/// <c>bootstrap-db</c> creates nothing.
/// </summary>
[Collection(SqlServerTests.Name)]
public sealed class ExistingSqlServerTests(SqlServerFixture sql)
{
    private static readonly Dictionary<string, string?> Existing = new() { ["Goldmark:Bootstrap:Mode"] = "Existing" };

    /// <summary>What a DBA runs before Mode Existing (docs/ops/external-sql-server.md).</summary>
    private static string DbaScript(Installation i) => $"""
        CREATE DATABASE {i.Database};
        CREATE LOGIN {i.RuntimeUser} WITH PASSWORD = N'{i.RuntimePassword}', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
        CREATE LOGIN {i.MigrationUser} WITH PASSWORD = N'{i.MigrationPassword}', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
        EXEC {i.Database}.sys.sp_executesql N'CREATE USER {i.RuntimeUser} FOR LOGIN {i.RuntimeUser};
            CREATE USER {i.MigrationUser} FOR LOGIN {i.MigrationUser};
            ALTER ROLE db_owner ADD MEMBER {i.MigrationUser};';
        """;

    [Fact]
    public async Task Mode_Existing_uses_what_the_DBA_created_and_creates_nothing()
    {
        var installation = sql.NewInstallation();
        await sql.AsAdminAsync("master", DbaScript(installation));

        var result = await sql.RunAsync(installation, ["bootstrap-db", "migrate"], Existing);

        Assert.True(result.Code == 0, result.ToString());
        Assert.Contains($"runtime login: using the existing login '{installation.RuntimeUser}' (Mode Existing: nothing is created)", result.Output, StringComparison.Ordinal);
        Assert.Contains("runtime login: marked database user as managed by Goldmark", result.Output, StringComparison.Ordinal);
        var bootstrapLines = result.Output.Split('\n').Where(l => l.StartsWith("bootstrap-db:", StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(bootstrapLines, l => l.Contains(": created", StringComparison.Ordinal) || l.Contains("changed login password", StringComparison.Ordinal)
            || l.Contains("added database user to db_owner", StringComparison.Ordinal));
        Assert.Contains("migrate: applied", result.Output, StringComparison.Ordinal);
        Assert.Equal(1, await sql.ScalarAsync<int>(installation.RuntimeUser, installation.RuntimePassword, installation.Database, "SELECT IS_ROLEMEMBER('goldmark_runtime');"));

        var again = await sql.RunAsync(installation, ["bootstrap-db", "migrate"], Existing);
        Assert.True(again.Code == 0, again.ToString());
        Assert.DoesNotContain("marked", again.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("database", "the database '{0}' does not exist, and this mode creates nothing")]
    [InlineData("login", "the runtime login '{1}' does not exist, and this mode creates nothing")]
    [InlineData("user", "the runtime login '{1}' has no user in '{0}'")]
    [InlineData("db_owner", "the migration login's user '{2}' is not a member of db_owner")]
    [InlineData("password", "the password of the runtime login '{1}' differs from GOLDMARK_DB_PASSWORD, and this mode never changes passwords")]
    public async Task Mode_Existing_refuses_with_nothing_changed_when_something_is_missing(string missing, string message)
    {
        var installation = sql.NewInstallation();
        var foreign = SqlServerFixture.Secret();
        var script = DbaScript(installation);
        script = missing switch
        {
            "database" => $"CREATE LOGIN {installation.RuntimeUser} WITH PASSWORD = N'{installation.RuntimePassword}';",
            "login" => script.Replace($"CREATE LOGIN {installation.RuntimeUser} WITH PASSWORD = N'{installation.RuntimePassword}', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;", "", StringComparison.Ordinal)
                .Replace($"CREATE USER {installation.RuntimeUser} FOR LOGIN {installation.RuntimeUser};", "", StringComparison.Ordinal),
            "user" => script.Replace($"CREATE USER {installation.RuntimeUser} FOR LOGIN {installation.RuntimeUser};", "", StringComparison.Ordinal),
            "db_owner" => script.Replace($"ALTER ROLE db_owner ADD MEMBER {installation.MigrationUser};", "", StringComparison.Ordinal),
            "password" => script.Replace($"N'{installation.RuntimePassword}'", $"N'{foreign}'", StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(missing)),
        };
        await sql.AsAdminAsync("master", script);

        var refused = await sql.RunAsync(installation, ["bootstrap-db", "migrate"], Existing);

        Assert.Equal(3, refused.Code);
        Assert.Contains(string.Format(System.Globalization.CultureInfo.InvariantCulture, message, installation.Database, installation.RuntimeUser, installation.MigrationUser),
            refused.Error, StringComparison.Ordinal);
        Assert.Contains("Nothing was changed.", refused.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("migrate:", refused.Output, StringComparison.Ordinal);
        if (missing == "password")
        {
            Assert.Equal(1, await sql.AdminScalarAsync<int>("master", $"SELECT PWDCOMPARE(N'{foreign}', password_hash) FROM sys.sql_logins WHERE name = N'{installation.RuntimeUser}';"));
        }

        if (missing != "database")
        {
            Assert.Equal(0, await sql.AdminScalarAsync<int>(installation.Database, "SELECT COUNT(*) FROM sys.extended_properties WHERE name = N'goldmark:managed';"));
        }
    }

    [Fact]
    public async Task A_dedicated_administrator_login_works_without_sa()
    {
        var installation = sql.NewInstallation();
        var dba = $"{installation.MigrationUser}_dba";
        var dbaPassword = SqlServerFixture.Secret();
        await sql.AsAdminAsync("master", $"CREATE LOGIN {dba} WITH PASSWORD = N'{dbaPassword}'; ALTER SERVER ROLE sysadmin ADD MEMBER {dba};");

        var result = await sql.RunAsync(installation, ["bootstrap-db", "migrate"], new Dictionary<string, string?>
        {
            ["Goldmark:Bootstrap:AdminUser"] = dba,
            ["Goldmark:Bootstrap:AdminPassword"] = dbaPassword,
            ["Goldmark:Bootstrap:AllowSaAdmin"] = "false",
        });

        Assert.True(result.Code == 0, result.ToString());
    }

    [Fact]
    public async Task Sa_is_refused_as_the_administrator_unless_allowed()
    {
        var installation = sql.NewInstallation();

        var refused = await sql.RunAsync(installation, ["bootstrap-db"], new Dictionary<string, string?> { ["Goldmark:Bootstrap:AllowSaAdmin"] = "false" });

        Assert.Equal(2, refused.Code);
        Assert.Contains("Goldmark:Bootstrap:AdminUser must not be 'sa'", refused.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_administrator_without_CONTROL_SERVER_is_refused_before_anything_is_created()
    {
        var installation = sql.NewInstallation();
        var weak = $"{installation.MigrationUser}_weak";
        var weakPassword = SqlServerFixture.Secret();
        await sql.AsAdminAsync("master",
            $"CREATE LOGIN {weak} WITH PASSWORD = N'{weakPassword}'; ALTER SERVER ROLE securityadmin ADD MEMBER {weak}; ALTER SERVER ROLE dbcreator ADD MEMBER {weak};");

        var refused = await sql.RunAsync(installation, ["bootstrap-db"], new Dictionary<string, string?>
        {
            ["Goldmark:Bootstrap:AdminUser"] = weak,
            ["Goldmark:Bootstrap:AdminPassword"] = weakPassword,
        });

        Assert.Equal(3, refused.Code);
        Assert.Contains($"the administrator login '{weak}' needs CONTROL SERVER", refused.Error, StringComparison.Ordinal);
        Assert.Equal(0, await sql.AdminScalarAsync<int>("master",
            $"SELECT COUNT(*) FROM sys.server_principals WHERE name IN (N'{installation.RuntimeUser}', N'{installation.MigrationUser}');"));
    }
}

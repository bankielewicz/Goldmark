using Goldmark.Api.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Goldmark.Api.IntegrationTests;

/// <summary>
/// The migration login is db_owner of the Goldmark database only (owner decision, slice 2):
/// no server roles, not sysadmin, no rights in other databases; and Goldmark only manages
/// logins it created.
/// </summary>
[Collection(SqlServerTests.Name)]
public sealed class MigrationLoginTests(SqlServerFixture sql)
{
    [Fact]
    public async Task The_migration_login_is_db_owner_of_its_database_and_nothing_more()
    {
        var (installation, result) = await sql.SharedAsync("migration-login");

        var flags = await sql.ScalarAsync<string>(installation.MigrationUser, installation.MigrationPassword, installation.Database, """
            SELECT CONCAT(IS_SRVROLEMEMBER('sysadmin'), IS_SRVROLEMEMBER('dbcreator'), IS_SRVROLEMEMBER('securityadmin'),
                IS_MEMBER('db_owner'),
                (SELECT COUNT(*) FROM sys.login_token WHERE type = N'SERVER ROLE' AND name <> N'public'),
                HAS_PERMS_BY_NAME(NULL, NULL, 'CREATE ANY DATABASE'), HAS_PERMS_BY_NAME(NULL, NULL, 'ALTER ANY LOGIN'));
            """);

        Assert.Equal("0001000", flags);
        Assert.Contains($"verified migration login '{installation.MigrationUser}' is db_owner", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("master")]
    [InlineData("msdb")]
    public async Task The_migration_login_has_no_rights_in_other_databases(string database)
    {
        var (installation, _) = await sql.SharedAsync("migration-login");

        var owner = await sql.ScalarAsync<int>(installation.MigrationUser, installation.MigrationPassword, database,
            "SELECT ISNULL(IS_MEMBER('db_owner'), 0) + ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CREATE TABLE'), 0);");

        Assert.Equal(0, owner);
    }

    [Fact]
    public async Task The_migration_login_cannot_create_logins_or_databases()
    {
        var (installation, _) = await sql.SharedAsync("migration-login");

        await Assert.ThrowsAsync<SqlException>(() => sql.ExecuteAsync(installation.MigrationUser, installation.MigrationPassword,
            installation.Database, $"CREATE LOGIN it_probe WITH PASSWORD = N'{SqlServerFixture.Secret()}';"));
        await Assert.ThrowsAsync<SqlException>(() => sql.ExecuteAsync(installation.MigrationUser, installation.MigrationPassword,
            installation.Database, "CREATE DATABASE GoldmarkItProbe;"));
    }

    [Fact]
    public async Task Both_database_users_carry_the_managed_marker_for_their_purpose()
    {
        var (installation, _) = await sql.SharedAsync("migration-login");

        var markers = await sql.AdminScalarAsync<string>(installation.Database, $"""
            SELECT STRING_AGG(CONCAT(USER_NAME(major_id), '=', CAST(value AS nvarchar(64))), ',') WITHIN GROUP (ORDER BY CAST(value AS nvarchar(64)))
            FROM sys.extended_properties WHERE class = 4 AND name = N'goldmark:managed';
            """);

        Assert.Equal($"{installation.MigrationUser}=migration-login,{installation.RuntimeUser}=runtime-login", markers);
    }

    [Fact]
    public async Task An_existing_migration_login_not_created_by_Goldmark_is_refused_with_nothing_changed()
    {
        var installation = sql.NewInstallation();
        var foreignPassword = SqlServerFixture.Secret();
        await sql.AsAdminAsync("master", $"CREATE LOGIN {installation.MigrationUser} WITH PASSWORD = N'{foreignPassword}';");

        var refused = await sql.RunAsync(installation, ["bootstrap-db", "migrate"]);

        Assert.Equal(3, refused.Code);
        Assert.Contains($"login '{installation.MigrationUser}' already exists and was not created by Goldmark", refused.Error, StringComparison.Ordinal);
        Assert.Contains("GOLDMARK_DB_MIGRATION_USER", refused.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("migrate:", refused.Output, StringComparison.Ordinal);
        Assert.Equal(0, await sql.AdminScalarAsync<int>("master", $"SELECT COUNT(*) FROM sys.databases WHERE name = N'{installation.Database}';"));
        Assert.Equal(0, await sql.AdminScalarAsync<int>("master", $"SELECT COUNT(*) FROM sys.server_principals WHERE name = N'{installation.RuntimeUser}';"));
        Assert.Equal(1, await sql.ScalarAsync<int>(installation.MigrationUser, foreignPassword, "master", "SELECT 1;"));

        var adopted = await sql.RunAsync(installation, ["bootstrap-db", "migrate"],
            new Dictionary<string, string?> { ["Goldmark:Bootstrap:AdoptExistingLogin"] = "true" });

        Assert.True(adopted.Code == 0, adopted.ToString());
        Assert.Contains($"migration login: adopting the existing login '{installation.MigrationUser}'", adopted.Output, StringComparison.Ordinal);
        Assert.Equal(1, await sql.ScalarAsync<int>(installation.MigrationUser, installation.MigrationPassword, installation.Database, "SELECT IS_MEMBER('db_owner');"));
    }

    [Fact]
    public async Task A_managed_login_cannot_be_reused_for_the_other_purpose()
    {
        var (installation, _) = await sql.ProvisionAsync();

        var swapped = await sql.RunAsync(installation, ["bootstrap-db"], new Dictionary<string, string?>
        {
            ["Goldmark:Database:User"] = installation.MigrationUser,
            ["Goldmark:Database:Password"] = installation.MigrationPassword,
            ["Goldmark:Migration:User"] = installation.RuntimeUser,
            ["Goldmark:Migration:Password"] = installation.RuntimePassword,
        });

        Assert.Equal(3, swapped.Code);
        Assert.Contains("is managed by Goldmark as the migration-login and cannot be used as the runtime login", swapped.Error, StringComparison.Ordinal);
        Assert.Equal(0, await sql.ScalarAsync<int>(installation.RuntimeUser, installation.RuntimePassword, installation.Database, "SELECT ISNULL(IS_MEMBER('db_owner'), 0);"));
    }

    [Fact]
    public async Task Bootstrap_refuses_a_migration_login_that_holds_a_server_role()
    {
        var (installation, _) = await sql.ProvisionAsync();
        await sql.AsAdminAsync("master", $"ALTER SERVER ROLE dbcreator ADD MEMBER {installation.MigrationUser};");
        try
        {
            var refused = await sql.RunAsync(installation, ["bootstrap-db"]);

            Assert.Equal(3, refused.Code);
            Assert.Contains("server role dbcreator", refused.Error, StringComparison.Ordinal);
        }
        finally
        {
            await sql.AsAdminAsync("master", $"ALTER SERVER ROLE dbcreator DROP MEMBER {installation.MigrationUser};");
        }

        Assert.Equal(0, (await sql.RunAsync(installation, ["bootstrap-db"])).Code);
    }

    [Fact]
    public async Task Changing_the_migration_password_rotates_it()
    {
        var (installation, _) = await sql.ProvisionAsync();
        var rotated = installation with { MigrationPassword = SqlServerFixture.Secret() };

        var result = await sql.RunAsync(rotated, ["bootstrap-db", "migrate"]);

        Assert.True(result.Code == 0, result.ToString());
        Assert.Contains("migration login: changed login password", result.Output, StringComparison.Ordinal);
        Assert.Equal(1, await sql.ScalarAsync<int>(rotated.MigrationUser, rotated.MigrationPassword, rotated.Database, "SELECT 1;"));
    }
}

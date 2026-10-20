using Goldmark.Api.Data;
using Goldmark.Api.IntegrationTests.Infrastructure;

namespace Goldmark.Api.IntegrationTests;

/// <summary><c>migrate</c> fails closed: a newer or diverged schema, or a runtime login with extra rights.</summary>
[Collection(SqlServerTests.Name)]
public sealed class MigrateRefusalTests(SqlServerFixture sql)
{
    [Fact]
    public async Task A_database_migrated_by_a_newer_build_is_refused_and_left_unchanged()
    {
        var (installation, _) = await sql.ProvisionAsync();
        await sql.AsAdminAsync(installation.Database,
            "INSERT INTO dbo.SchemaVersions (ScriptName, Applied) VALUES (N'Goldmark.Migrations.9999_future.sql', SYSUTCDATETIME());");

        var refused = await sql.RunAsync(installation, ["migrate"]);

        Assert.Equal(3, refused.Code);
        Assert.Contains("Goldmark.Migrations.9999_future.sql", refused.Error, StringComparison.Ordinal);
        Assert.Contains($"database schema version 9999, this build: {SchemaMigrations.ExpectedVersion}", refused.Error, StringComparison.Ordinal);
        Assert.Equal(SchemaMigrations.Scripts.Count + 1, await sql.AdminScalarAsync<int>(installation.Database, "SELECT COUNT(*) FROM dbo.SchemaVersions;"));
    }

    [Theory]
    [InlineData("GRANT ALTER ON SCHEMA::cmdb TO {0};", "permission on schema cmdb ALTER")]
    [InlineData("GRANT UPDATE ON OBJECT::cmdb.AuditLog TO {0};", null)] // the role's DENY still wins: effective rights unchanged
    [InlineData("REVOKE DELETE ON OBJECT::cmdb.AuditLog FROM goldmark_runtime;", "permission on cmdb.AuditLog DELETE")]
    // A column-level GRANT to the role that holds the table-level DENY overrides it (Codex on #14);
    // the same GRANT to the user does not (the role's DENY wins), so it changes nothing.
    [InlineData("GRANT UPDATE ON OBJECT::cmdb.AuditLog (Action) TO goldmark_runtime;", "permission on cmdb.AuditLog column Action UPDATE")]
    [InlineData("GRANT UPDATE ON OBJECT::cmdb.AuditLog (Action) TO {0};", null)]
    [InlineData("REVOKE DELETE ON OBJECT::cmdb.Installation FROM goldmark_runtime;", "permission on cmdb.Installation DELETE")]
    [InlineData("GRANT UPDATE ON OBJECT::cmdb.Installation (SetupCompletedBy) TO goldmark_runtime;", "permission on cmdb.Installation column SetupCompletedBy UPDATE")]
    [InlineData("GRANT INSERT ON OBJECT::dbo.SchemaVersions TO {0};", "permission on dbo.SchemaVersions INSERT")]
    [InlineData("ALTER ROLE db_datawriter ADD MEMBER {0};", "database role db_datawriter")]
    [InlineData("GRANT CREATE TABLE TO {0};", "database permission CREATE TABLE")]
    // A revoked grant must fail here, not later in the application (Codex on #14).
    [InlineData("REVOKE SELECT ON SCHEMA::cmdb FROM goldmark_runtime;", "missing permission on schema cmdb SELECT")]
    [InlineData("REVOKE UPDATE ON SCHEMA::cmdb FROM goldmark_runtime;", "missing permission on schema cmdb UPDATE")]
    [InlineData("DENY INSERT ON OBJECT::cmdb.AuditLog TO goldmark_runtime;", "missing permission on cmdb.AuditLog INSERT")]
    [InlineData("DENY INSERT ON OBJECT::cmdb.Installation TO goldmark_runtime;", "missing permission on cmdb.Installation INSERT")]
    [InlineData("REVOKE SELECT ON OBJECT::dbo.SchemaVersions FROM goldmark_runtime;", "missing permission on dbo.SchemaVersions SELECT")]
    public async Task Migrate_refuses_a_runtime_login_whose_effective_rights_differ_from_its_role(string change, string? finding)
    {
        var (installation, _) = await sql.ProvisionAsync();
        await sql.AsAdminAsync(installation.Database, string.Format(System.Globalization.CultureInfo.InvariantCulture, change, installation.RuntimeUser));

        var result = await sql.RunAsync(installation, ["migrate"]);

        if (finding is null)
        {
            Assert.True(result.Code == 0, result.ToString());
        }
        else
        {
            Assert.Equal(3, result.Code);
            Assert.Contains(finding, result.Error, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_column_level_grant_to_the_role_overrides_its_audit_log_DENY_so_migrate_checks_every_column()
    {
        var (installation, _) = await sql.ProvisionAsync();
        await sql.ExecuteAsync(installation.RuntimeUser, installation.RuntimePassword, installation.Database,
            "INSERT INTO cmdb.AuditLog (ActorType, Action) VALUES ('System', 'probe.column');");
        await sql.AsAdminAsync(installation.Database, "GRANT UPDATE ON OBJECT::cmdb.AuditLog (Action) TO goldmark_runtime;");

        // SQL Server: a column-level GRANT wins over the same principal's table-level DENY.
        await sql.ExecuteAsync(installation.RuntimeUser, installation.RuntimePassword, installation.Database,
            "UPDATE cmdb.AuditLog SET Action = 'probe.tampered' WHERE Action = 'probe.column';");
        var refused = await sql.RunAsync(installation, ["migrate"]);

        Assert.Equal(3, refused.Code);
        Assert.Contains("permission on cmdb.AuditLog column Action UPDATE", refused.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Migrate_refuses_a_migration_login_with_a_server_role_before_touching_the_database()
    {
        var installation = sql.NewInstallation();
        Assert.Equal(0, (await sql.RunAsync(installation, ["bootstrap-db"])).Code);
        await sql.AsAdminAsync("master", $"ALTER SERVER ROLE dbcreator ADD MEMBER {installation.MigrationUser};");
        try
        {
            var refused = await sql.RunAsync(installation, ["migrate"]);

            Assert.Equal(3, refused.Code);
            Assert.Contains("server role dbcreator", refused.Error, StringComparison.Ordinal);
            Assert.Equal(0, await sql.AdminScalarAsync<int>(installation.Database, "SELECT COUNT(*) FROM sys.tables WHERE name = N'SchemaVersions';"));
        }
        finally
        {
            await sql.AsAdminAsync("master", $"ALTER SERVER ROLE dbcreator DROP MEMBER {installation.MigrationUser};");
        }
    }

    [Fact]
    public async Task Migrate_refuses_an_unmanaged_db_owner()
    {
        var installation = sql.NewInstallation();
        Assert.Equal(0, (await sql.RunAsync(installation, ["bootstrap-db"])).Code);
        var rogue = $"{installation.MigrationUser}_rogue";
        var roguePassword = SqlServerFixture.Secret();
        await sql.AsAdminAsync(installation.Database,
            $"CREATE LOGIN {rogue} WITH PASSWORD = N'{roguePassword}'; CREATE USER {rogue} FOR LOGIN {rogue}; ALTER ROLE db_owner ADD MEMBER {rogue};");

        var refused = await sql.RunAsync(installation, ["migrate"], new Dictionary<string, string?>
        {
            ["Goldmark:Migration:User"] = rogue,
            ["Goldmark:Migration:Password"] = roguePassword,
        });

        Assert.Equal(3, refused.Code);
        Assert.Contains("missing marker goldmark:managed = migration-login", refused.Error, StringComparison.Ordinal);
        Assert.Equal(0, await sql.AdminScalarAsync<int>(installation.Database, "SELECT COUNT(*) FROM sys.tables WHERE name = N'SchemaVersions';"));
    }

    [Fact]
    public async Task Migrate_without_bootstrap_does_not_create_logins()
    {
        var installation = sql.NewInstallation();

        var result = await sql.RunAsync(installation, ["migrate"]);

        Assert.Equal(1, result.Code);
        Assert.Contains("SQL Server error 18456", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, await sql.AdminScalarAsync<int>("master",
            $"SELECT COUNT(*) FROM sys.server_principals WHERE name IN (N'{installation.RuntimeUser}', N'{installation.MigrationUser}');"));
    }

    [Fact]
    public async Task A_chain_stops_at_the_first_failing_command()
    {
        var installation = sql.NewInstallation();

        var result = await sql.RunAsync(installation, ["bootstrap-db", "migrate"],
            new Dictionary<string, string?> { ["Goldmark:Migration:Password"] = installation.RuntimePassword });

        Assert.Equal(2, result.Code);
        Assert.Contains("Goldmark:Migration:Password must differ from Goldmark:Database:Password", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("migrate:", result.Output + result.Error, StringComparison.Ordinal);
    }
}

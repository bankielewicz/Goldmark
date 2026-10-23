using Goldmark.Api.Data;
using Goldmark.Api.IntegrationTests.Infrastructure;

namespace Goldmark.Api.IntegrationTests;

/// <summary>Slice 2 "done when": migrations are idempotent and build the baseline schema.</summary>
[Collection(SqlServerTests.Name)]
public sealed class MigrationTests(SqlServerFixture sql)
{
    [Fact]
    public async Task Bootstrap_and_migrate_build_the_schema_at_the_version_this_build_expects()
    {
        var (installation, result) = await sql.ProvisionAsync();

        Assert.Contains($"applied {SchemaMigrations.Scripts.Count} migration(s)", result.Output, StringComparison.Ordinal);
        Assert.Contains($"database schema version {SchemaMigrations.ExpectedVersion} (this build expects {SchemaMigrations.ExpectedVersion})",
            result.Output, StringComparison.Ordinal);
        var applied = await sql.AdminScalarAsync<string>(installation.Database,
            "SELECT STRING_AGG(ScriptName, ',') WITHIN GROUP (ORDER BY ScriptName) FROM dbo.SchemaVersions;");
        Assert.Equal(string.Join(',', SchemaMigrations.Scripts), applied);
    }

    [Theory]
    [InlineData("Tenant")]
    [InlineData("CiType")]
    [InlineData("Ci")]
    [InlineData("RelationshipType")]
    [InlineData("Relationship")]
    [InlineData("AuditLog")]
    [InlineData("Role")]
    [InlineData("UserAccount")]
    [InlineData("UserRole")]
    [InlineData("RecoveryCode")]
    [InlineData("UserInvitation")]
    [InlineData("UserSession")]
    [InlineData("Installation")]
    public async Task The_application_tables_exist_in_the_cmdb_schema(string table)
    {
        var (installation, _) = await sql.SharedAsync("schema");

        var exists = await sql.AdminScalarAsync<int>(installation.Database, $"SELECT COUNT(*) FROM sys.tables WHERE object_id = OBJECT_ID(N'cmdb.{table}');");

        Assert.Equal(1, exists);
    }

    [Theory]
    [InlineData("Ci", "CiHistory")]
    [InlineData("Relationship", "RelationshipHistory")]
    [InlineData("CiType", "CiTypeHistory")] // the definitions too (0007, DEC-045 item 8)
    [InlineData("AttributeDef", "AttributeDefHistory")]
    [InlineData("RelationshipType", "RelationshipTypeHistory")]
    public async Task CIs_relationships_and_their_definitions_are_system_versioned(string table, string history)
    {
        var (installation, _) = await sql.SharedAsync("schema");

        var state = await sql.AdminScalarAsync<string>(installation.Database, $"""
            SELECT CONCAT(t.temporal_type, ':', OBJECT_SCHEMA_NAME(t.history_table_id), '.', OBJECT_NAME(t.history_table_id))
            FROM sys.tables AS t WHERE t.object_id = OBJECT_ID(N'cmdb.{table}');
            """);

        Assert.Equal($"2:cmdb.{history}", state);
    }

    [Fact]
    public async Task Schema_and_role_are_owned_by_dbo_not_by_the_migration_login()
    {
        var (installation, _) = await sql.SharedAsync("schema");

        var owners = await sql.AdminScalarAsync<string>(installation.Database, """
            SELECT CONCAT(USER_NAME(s.principal_id), ',', USER_NAME(r.owning_principal_id))
            FROM sys.schemas AS s CROSS JOIN sys.database_principals AS r
            WHERE s.name = N'cmdb' AND r.name = N'goldmark_runtime';
            """);

        Assert.Equal("dbo,dbo", owners);
    }

    [Fact]
    public async Task Running_db_init_again_changes_nothing()
    {
        var (installation, _) = await sql.ProvisionAsync();
        var before = await JournalAsync(installation);

        var again = await sql.RunAsync(installation, ["bootstrap-db", "migrate"]);

        Assert.True(again.Code == 0, again.ToString());
        Assert.Contains("migrate: no migrations to apply", again.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("created", again.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("added", again.Output, StringComparison.Ordinal);
        Assert.Equal(before, await JournalAsync(installation));
    }

    [Fact]
    public async Task Concurrent_migrate_runs_apply_each_script_exactly_once()
    {
        var installation = sql.NewInstallation();
        var bootstrap = await sql.RunAsync(installation, ["bootstrap-db"]);
        Assert.True(bootstrap.Code == 0, bootstrap.ToString());

        var runs = await Task.WhenAll(sql.RunAsync(installation, ["migrate"]), sql.RunAsync(installation, ["migrate"]));

        Assert.All(runs, r => Assert.True(r.Code == 0, r.ToString()));
        var rows = await sql.AdminScalarAsync<int>(installation.Database, "SELECT COUNT(*) FROM dbo.SchemaVersions;");
        Assert.Equal(SchemaMigrations.Scripts.Count, rows);
    }

    [Fact]
    public async Task The_default_tenant_exists()
    {
        var (installation, _) = await sql.SharedAsync("schema");

        var name = await sql.AdminScalarAsync<string>(installation.Database,
            "SELECT Name FROM cmdb.Tenant WHERE Id = '00000000-0000-0000-0000-000000000001';");

        Assert.Equal("Default", name);
    }

    private Task<string> JournalAsync(Installation installation) =>
        sql.AdminScalarAsync<string>(installation.Database,
            "SELECT STRING_AGG(CONCAT(Id, ':', ScriptName, ':', CONVERT(varchar(30), Applied, 126)), ',') FROM dbo.SchemaVersions;");
}

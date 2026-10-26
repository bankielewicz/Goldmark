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

    /// <summary>
    /// 0008 (DEC-046 item 13) fills CreatedAt from each relationship's earliest version without versioning the rows
    /// it fills: their ValidFrom and the history stay as they were. Measured on a database taken back to 0007 with
    /// relationships in it, one of them changed since it was added.
    /// </summary>
    [Fact]
    public async Task Migration_0008_backfills_CreatedAt_without_writing_history()
    {
        var (installation, _) = await sql.ProvisionAsync();
        var db = installation.Database;
        await sql.AsAdminAsync(db, """
            ALTER TABLE cmdb.Relationship SET (SYSTEM_VERSIONING = OFF);
            ALTER TABLE cmdb.Relationship DROP CONSTRAINT DF_Relationship_CreatedAt;
            ALTER TABLE cmdb.Relationship DROP COLUMN CreatedAt;
            ALTER TABLE cmdb.RelationshipHistory DROP COLUMN CreatedAt;
            ALTER TABLE cmdb.Relationship SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = cmdb.RelationshipHistory, DATA_CONSISTENCY_CHECK = ON));
            DELETE FROM dbo.SchemaVersions WHERE ScriptName = N'Goldmark.Migrations.0008_relationship_created_at.sql';
            """);
        await sql.AsAdminAsync(db, """
            INSERT INTO cmdb.Ci (Id, TenantId, CiTypeId, Name, LifecycleState, Attributes, ChangedBy)
            SELECT v.Id, '00000000-0000-0000-0000-000000000001', t.Id, v.Name, 'Active', '{}', 'test'
            FROM (VALUES ('10000000-0000-0000-0000-000000000001', N'vm-1', N'VirtualMachine'),
                ('10000000-0000-0000-0000-000000000002', N'vm-2', N'VirtualMachine'),
                ('10000000-0000-0000-0000-000000000003', N'esx-1', N'HypervisorHost')) AS v (Id, Name, Type)
            JOIN cmdb.CiType AS t ON t.Name = v.Type;
            INSERT INTO cmdb.Relationship (Id, TenantId, FromCiId, ToCiId, RelationshipTypeId, ChangedBy)
            SELECT v.Id, '00000000-0000-0000-0000-000000000001', v.FromCi, '10000000-0000-0000-0000-000000000003', r.Id, 'test'
            FROM (VALUES ('20000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001'),
                ('20000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000002')) AS v (Id, FromCi)
            CROSS JOIN cmdb.RelationshipType AS r WHERE r.Name = N'runs_on';
            """);
        await sql.AsAdminAsync(db, """
            WAITFOR DELAY '00:00:00.050';
            UPDATE cmdb.Relationship SET RedundancyGroup = N'g', ChangedBy = 'test' WHERE Id = '20000000-0000-0000-0000-000000000002';
            """);
        const string Versions = """
            SELECT STRING_AGG(CONCAT(Id, ':', CONVERT(varchar(30), ValidFrom, 126)), ',') WITHIN GROUP (ORDER BY Id, ValidFrom)
            FROM cmdb.Relationship FOR SYSTEM_TIME ALL;
            """;
        var before = await sql.AdminScalarAsync<string>(db, Versions);

        var migrated = await sql.RunAsync(installation, ["migrate"]);

        Assert.True(migrated.Code == 0, migrated.ToString());
        Assert.Contains("applied 1 migration(s)", migrated.Output, StringComparison.Ordinal);
        Assert.Equal(before, await sql.AdminScalarAsync<string>(db, Versions));
        // vm-1 was never changed: its one version's ValidFrom. vm-2's current row and its history row: its first version's.
        var created = await sql.AdminScalarAsync<string>(db, """
            WITH Versions AS
            (
                SELECT Id, ValidFrom, CreatedAt, MIN(ValidFrom) OVER (PARTITION BY Id) AS First
                FROM cmdb.Relationship FOR SYSTEM_TIME ALL
            )
            SELECT STRING_AGG(CONCAT(Id, ':', IIF(CreatedAt = First, 'first', 'other')), ',') WITHIN GROUP (ORDER BY Id, ValidFrom)
            FROM Versions;
            """);
        Assert.Equal("20000000-0000-0000-0000-000000000001:first,20000000-0000-0000-0000-000000000002:first,20000000-0000-0000-0000-000000000002:first", created);
        Assert.Equal("2:cmdb.RelationshipHistory", await sql.AdminScalarAsync<string>(db, """
            SELECT CONCAT(t.temporal_type, ':', OBJECT_SCHEMA_NAME(t.history_table_id), '.', OBJECT_NAME(t.history_table_id))
            FROM sys.tables AS t WHERE t.object_id = OBJECT_ID(N'cmdb.Relationship');
            """));

        // A relationship added afterwards gets its date from the default.
        await sql.AsAdminAsync(db, """
            INSERT INTO cmdb.Relationship (Id, TenantId, FromCiId, ToCiId, RelationshipTypeId, ChangedBy)
            SELECT '20000000-0000-0000-0000-000000000003', '00000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001',
                '10000000-0000-0000-0000-000000000002', r.Id, 'test'
            FROM cmdb.RelationshipType AS r WHERE r.Name = N'depends_on';
            """);
        Assert.Equal(1, await sql.AdminScalarAsync<int>(db,
            "SELECT COUNT(*) FROM cmdb.Relationship WHERE Id = '20000000-0000-0000-0000-000000000003' AND CreatedAt IS NOT NULL;"));
    }

    private Task<string> JournalAsync(Installation installation) =>
        sql.AdminScalarAsync<string>(installation.Database,
            "SELECT STRING_AGG(CONCAT(Id, ':', ScriptName, ':', CONVERT(varchar(30), Applied, 126)), ',') FROM dbo.SchemaVersions;");
}

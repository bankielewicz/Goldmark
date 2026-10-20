using Goldmark.Api.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Goldmark.Api.IntegrationTests;

/// <summary>
/// Slice 2 "done when": the runtime login cannot run DDL, and cannot UPDATE or DELETE the audit
/// log (NFR-SEC-4, US-07). Each statement runs as the runtime login against real SQL Server.
/// </summary>
[Collection(SqlServerTests.Name)]
public sealed class RuntimePermissionTests(SqlServerFixture sql)
{
    private const string Shared = "runtime-permissions";

    public static TheoryData<string> Denied => new()
    {
        // DDL of any kind
        "CREATE TABLE cmdb.Probe (x int);",
        "CREATE TABLE dbo.Probe (x int);",
        "CREATE SCHEMA probe;",
        "DROP TABLE cmdb.Tenant;",
        "ALTER TABLE cmdb.AuditLog ADD Probe int NULL;",
        "CREATE INDEX IX_Probe ON cmdb.AuditLog (Action);",
        // History cannot be switched off or rewritten
        "ALTER TABLE cmdb.Ci SET (SYSTEM_VERSIONING = OFF);",
        "ALTER TABLE cmdb.Relationship SET (SYSTEM_VERSIONING = OFF);",
        "DELETE FROM cmdb.CiHistory;",
        "UPDATE cmdb.RelationshipHistory SET ChangedBy = N'x';",
        // The audit log is append-only
        "UPDATE cmdb.AuditLog SET Action = 'tampered';",
        "DELETE FROM cmdb.AuditLog;",
        "TRUNCATE TABLE cmdb.AuditLog;",
        "MERGE cmdb.AuditLog AS t USING (SELECT 1 AS x) AS s ON 1 = 1 WHEN MATCHED THEN DELETE;",
        "SET IDENTITY_INSERT cmdb.AuditLog ON;",
        // First-run setup stays done (US-02): the setup-complete row cannot change or go
        "UPDATE cmdb.Installation SET SetupCompletedAt = SYSUTCDATETIME();",
        "DELETE FROM cmdb.Installation;",
        "TRUNCATE TABLE cmdb.Installation;",
        // The audit retention period is migrate's (DEC-041): the runtime login can only read it, and it
        // cannot change or bypass the purge procedure (dynamic SQL is outside the ownership chain)
        "UPDATE cmdb.AuditRetention SET RetentionDays = 730;",
        "DELETE FROM cmdb.AuditRetention;",
        "ALTER PROCEDURE cmdb.PurgeAuditLog AS SELECT 1;",
        "EXEC sys.sp_executesql N'DELETE FROM cmdb.AuditLog;';",
        // The migration journal is read-only
        "UPDATE dbo.SchemaVersions SET ScriptName = ScriptName;",
        "INSERT INTO dbo.SchemaVersions (ScriptName, Applied) VALUES (N'Goldmark.Migrations.9999_probe.sql', SYSUTCDATETIME());",
        "DELETE FROM dbo.SchemaVersions;",
        // No security administration
        "GRANT SELECT ON SCHEMA::cmdb TO public;",
        "ALTER ROLE goldmark_runtime ADD MEMBER guest;",
        "CREATE USER probe WITHOUT LOGIN;",
    };

    [Theory]
    [MemberData(nameof(Denied))]
    public async Task The_runtime_login_is_denied(string statement)
    {
        var (installation, _) = await sql.SharedAsync(Shared);

        var ex = await Assert.ThrowsAsync<SqlException>(() =>
            sql.ExecuteAsync(installation.RuntimeUser, installation.RuntimePassword, installation.Database, statement));

        Assert.True(ex.Number != 0);
        Assert.Equal(SchemaChecks.Baseline, await SchemaChecks.FingerprintAsync(sql, installation));
    }

    [Fact]
    public async Task The_runtime_login_can_append_to_and_read_the_audit_log()
    {
        var (installation, _) = await sql.SharedAsync(Shared);

        await sql.ExecuteAsync(installation.RuntimeUser, installation.RuntimePassword, installation.Database,
            "INSERT INTO cmdb.AuditLog (ActorType, ActorId, Action) VALUES ('System', N'test', 'probe.append');");
        var count = await sql.ScalarAsync<int>(installation.RuntimeUser, installation.RuntimePassword, installation.Database,
            "SELECT COUNT(*) FROM cmdb.AuditLog WHERE Action = 'probe.append';");

        Assert.True(count >= 1);
    }

    [Fact]
    public async Task The_runtime_login_has_DML_on_application_tables_and_history_is_kept()
    {
        var (installation, _) = await sql.SharedAsync(Shared);
        var name = $"probe-{Guid.NewGuid():N}";

        await sql.ExecuteAsync(installation.RuntimeUser, installation.RuntimePassword, installation.Database, $"""
            INSERT INTO cmdb.CiType (Name) VALUES (N'{name}');
            DECLARE @type int = SCOPE_IDENTITY(), @id uniqueidentifier = NEWID();
            INSERT INTO cmdb.Ci (Id, TenantId, CiTypeId, Name, LifecycleState, ChangedBy)
            VALUES (@id, '00000000-0000-0000-0000-000000000001', @type, N'{name}', 'Active', N'test');
            UPDATE cmdb.Ci SET LifecycleState = 'Retired', Attributes = '{"{"}"probe": true{"}"}' WHERE Id = @id;
            DELETE FROM cmdb.Ci WHERE Id = @id;
            DELETE FROM cmdb.CiType WHERE Id = @type;
            """);
        // The update and the delete each moved the previous version into the history table.
        var history = await sql.ScalarAsync<int>(installation.RuntimeUser, installation.RuntimePassword, installation.Database,
            $"SELECT COUNT(*) FROM cmdb.CiHistory WHERE Name = N'{name}';");

        Assert.Equal(2, history);
    }

    [Fact]
    public async Task The_runtime_login_can_read_the_migration_journal()
    {
        var (installation, _) = await sql.SharedAsync(Shared);

        var rows = await sql.ScalarAsync<int>(installation.RuntimeUser, installation.RuntimePassword, installation.Database,
            "SELECT COUNT(*) FROM dbo.SchemaVersions;");

        Assert.True(rows > 0);
    }

    [Fact]
    public async Task The_runtime_login_is_not_sysadmin_or_db_owner_and_holds_no_server_role()
    {
        var (installation, _) = await sql.SharedAsync(Shared);

        var flags = await sql.ScalarAsync<string>(installation.RuntimeUser, installation.RuntimePassword, installation.Database, """
            SELECT CONCAT(IS_SRVROLEMEMBER('sysadmin'), IS_MEMBER('db_owner'), IS_MEMBER('db_ddladmin'),
                IS_MEMBER('goldmark_runtime'),
                (SELECT COUNT(*) FROM sys.login_token WHERE type = N'SERVER ROLE' AND name <> N'public'));
            """);

        Assert.Equal("00010", flags);
    }
}

/// <summary>A cheap fingerprint of the schema, to prove a denied statement changed nothing.</summary>
internal static class SchemaChecks
{
    public const string Baseline = "ok";

    public static async Task<string> FingerprintAsync(SqlServerFixture sql, Installation installation)
    {
        var state = await sql.AdminScalarAsync<string>(installation.Database, """
            SELECT CONCAT(
                (SELECT COUNT(*) FROM sys.tables WHERE SCHEMA_NAME(schema_id) IN (N'cmdb', N'dbo')),
                ':', (SELECT COUNT(*) FROM sys.tables WHERE temporal_type = 2),
                ':', (SELECT COUNT(*) FROM sys.schemas WHERE name = N'probe'),
                ':', (SELECT COUNT(*) FROM sys.database_principals WHERE name = N'probe'),
                ':', (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE ScriptName LIKE N'%probe%'),
                ':', IS_ROLEMEMBER(N'goldmark_runtime', N'guest'),
                ':', (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'cmdb.AuditLog') AND name = N'Probe'),
                ':', (SELECT COUNT(*) FROM cmdb.AuditLog WHERE Action = 'tampered'));
            """);
        const string expected = "17:2:0:0:0:0:0:0";
        return state == expected ? Baseline : state;
    }
}

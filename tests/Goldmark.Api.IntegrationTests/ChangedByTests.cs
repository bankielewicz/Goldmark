using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Claims;
using Goldmark.Api.Auth;
using Goldmark.Api.Data;
using Goldmark.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Goldmark.Api.IntegrationTests;

/// <summary>
/// FR-CM-5 against SQL Server 2025 (DEC-047): who changed a CI or a relationship comes from the connection's
/// <c>SESSION_CONTEXT</c>, set by <see cref="DbSession"/> for the signed-in person, through the columns'
/// defaults (migration 0009). Measured here because the design rests on it: the key is read-only once set, a
/// pooled connection starts empty, a write by nobody fails, and the migration moves no version.
/// </summary>
[Collection(SqlServerTests.Name)]
public sealed class ChangedByTests(SqlServerFixture sql)
{
    private const string WhoSql = "SELECT CONCAT(@@SPID, ':', CONVERT(nvarchar(256), SESSION_CONTEXT(N'Goldmark.ChangedBy')));";

    private const string NameSomeoneElseSql = "EXEC sys.sp_set_session_context @key = N'Goldmark.ChangedBy', @value = N'someone else';";

    private const string InsertCiSql = """
        INSERT INTO cmdb.Ci (Id, TenantId, CiTypeId, Name, LifecycleState, Attributes)
        SELECT @id, '00000000-0000-0000-0000-000000000001', t.Id, @name, 'Active', '{}'
        FROM cmdb.CiType AS t WHERE t.Name = N'Server';
        """;

    private const string RenameCiSql = "UPDATE cmdb.Ci SET Name = @name, ChangedBy = DEFAULT WHERE Id = @id;";

    private const string ChangedBySql = "SELECT ChangedBy FROM cmdb.Ci WHERE Id = @id;";

    private const string VersionsSql = """
        SELECT STRING_AGG(CONCAT(Name, IIF(ValidFrom = ValidTo, ' (no time)', '')), ',') WITHIN GROUP (ORDER BY ValidFrom, ValidTo)
        FROM cmdb.Ci FOR SYSTEM_TIME ALL WHERE Id = @id;
        """;

    private const string HistoryRowsSql = "SELECT COUNT(*) FROM cmdb.CiHistory WHERE Id = @id;";

    [Fact]
    public async Task The_acting_person_is_set_once_per_connection_and_a_reused_connection_starts_with_nobody()
    {
        var (installation, _) = await sql.ProvisionAsync();
        var factory = new SqlConnectionFactory(Options.Create(sql.DatabaseOptions(installation)));
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var ci = Guid.NewGuid();

        string spid;
        await using (var db = new DbSession(factory, Acting(first)))
        {
            var who = await One<string>(db, WhoSql);
            spid = who!.Split(':')[0];
            Assert.Equal($"{spid}:{first:D}", who);
            db.RequireActor();

            // Read-only for the rest of the connection's use: nothing later can name someone else.
            var refused = await Assert.ThrowsAsync<SqlException>(() => Run(db, NameSomeoneElseSql));
            Assert.Equal(15664, refused.Number);

            // The column's default takes it, on INSERT and on UPDATE, as the runtime login.
            await Run(db, InsertCiSql, new { id = ci, name = "srv-a" });
            Assert.Equal(first.ToString("D"), await One<string>(db, ChangedBySql, new { id = ci }));
        }

        // The same physical connection, back from the pool, for a session with no principal: nobody is named,
        // and a write that needs a ChangedBy fails instead of recording the last person.
        await using (var db = new DbSession(factory))
        {
            Assert.Equal($"{spid}:", await One<string>(db, WhoSql));
            Assert.Throws<InvalidOperationException>(db.RequireActor);
            var refused = await Assert.ThrowsAsync<SqlException>(() => Run(db, RenameCiSql, new { id = ci, name = "srv-nobody" }));
            Assert.Equal(515, refused.Number); // cannot insert NULL into ChangedBy
            refused = await Assert.ThrowsAsync<SqlException>(() => Run(db, InsertCiSql, new { id = Guid.NewGuid(), name = "srv-nobody" }));
            Assert.Equal(515, refused.Number);
        }

        // And for another person: theirs, although the key was read-only on this connection a moment ago.
        await using (var db = new DbSession(factory, Acting(second)))
        {
            Assert.Equal($"{spid}:{second:D}", await One<string>(db, WhoSql));
            await Run(db, RenameCiSql, new { id = ci, name = "srv-b" });
            Assert.Equal(second.ToString("D"), await One<string>(db, ChangedBySql, new { id = ci }));

            // The runtime login reads the history through FOR SYSTEM_TIME and from the history table itself.
            Assert.Equal("srv-a,srv-b", await One<string>(db, VersionsSql, new { id = ci }));
            Assert.Equal(1, await One<int>(db, HistoryRowsSql, new { id = ci }));

            // Two changes in one transaction: the first one's version lasted no time. The history table keeps it, and
            // FOR SYSTEM_TIME ALL leaves it out (measured on SQL Server 2025, 2026-11-18), so a CI's history, which
            // reads through FOR SYSTEM_TIME, shows only versions that were ever in force.
            await db.InTransactionAsync(async () =>
            {
                await Run(db, RenameCiSql, new { id = ci, name = "srv-c" });
                await Run(db, RenameCiSql, new { id = ci, name = "srv-d" });
                return 0;
            }, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal("srv-a,srv-b,srv-d", await One<string>(db, VersionsSql, new { id = ci }));
            Assert.Equal(3, await One<int>(db, HistoryRowsSql, new { id = ci }));
        }
    }

    [Fact]
    public async Task Migration_0009_adds_the_defaults_and_the_indexes_without_moving_a_version()
    {
        var (installation, _) = await sql.ProvisionAsync();
        var db = installation.Database;
        await sql.AsAdminAsync(db, """
            ALTER TABLE cmdb.Ci DROP CONSTRAINT DF_Ci_ChangedBy;
            ALTER TABLE cmdb.Relationship DROP CONSTRAINT DF_Relationship_ChangedBy;
            DROP INDEX IX_CiHistory_Id ON cmdb.CiHistory;
            DROP INDEX IX_RelationshipHistory_FromCi ON cmdb.RelationshipHistory;
            DROP INDEX IX_RelationshipHistory_ToCi ON cmdb.RelationshipHistory;
            DROP INDEX IX_AuditLog_Target ON cmdb.AuditLog;
            DELETE FROM dbo.SchemaVersions WHERE ScriptName = N'Goldmark.Migrations.0009_changed_by_and_history.sql';
            """);
        await sql.AsAdminAsync(db, """
            INSERT INTO cmdb.Ci (Id, TenantId, CiTypeId, Name, LifecycleState, Attributes, ChangedBy)
            SELECT v.Id, '00000000-0000-0000-0000-000000000001', t.Id, v.Name, 'Active', '{}', 'test'
            FROM (VALUES ('10000000-0000-0000-0000-000000000001', N'vm-1', N'VirtualMachine'),
                ('10000000-0000-0000-0000-000000000003', N'esx-1', N'HypervisorHost')) AS v (Id, Name, Type)
            JOIN cmdb.CiType AS t ON t.Name = v.Type;
            INSERT INTO cmdb.Relationship (Id, TenantId, FromCiId, ToCiId, RelationshipTypeId, ChangedBy)
            SELECT '20000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000001',
                '10000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000003', r.Id, 'test'
            FROM cmdb.RelationshipType AS r WHERE r.Name = N'runs_on';
            """);
        await sql.AsAdminAsync(db, """
            WAITFOR DELAY '00:00:00.050';
            UPDATE cmdb.Ci SET Name = N'vm-1b', ChangedBy = 'test' WHERE Id = '10000000-0000-0000-0000-000000000001';
            UPDATE cmdb.Relationship SET RedundancyGroup = N'g', ChangedBy = 'test' WHERE Id = '20000000-0000-0000-0000-000000000001';
            """);
        const string Versions = """
            SELECT CONCAT(
                (SELECT STRING_AGG(CONCAT(Id, ':', CONVERT(varchar(30), ValidFrom, 126), ':', ChangedBy), ',') WITHIN GROUP (ORDER BY Id, ValidFrom)
                    FROM cmdb.Ci FOR SYSTEM_TIME ALL), '|',
                (SELECT STRING_AGG(CONCAT(Id, ':', CONVERT(varchar(30), ValidFrom, 126), ':', ChangedBy), ',') WITHIN GROUP (ORDER BY Id, ValidFrom)
                    FROM cmdb.Relationship FOR SYSTEM_TIME ALL));
            """;
        var before = await sql.AdminScalarAsync<string>(db, Versions);

        var migrated = await sql.RunAsync(installation, ["migrate"]);

        Assert.True(migrated.Code == 0, migrated.ToString());
        Assert.Contains("applied 1 migration(s)", migrated.Output, StringComparison.Ordinal);
        Assert.Equal(before, await sql.AdminScalarAsync<string>(db, Versions));
        Assert.Equal("DF_Ci_ChangedBy,DF_Relationship_ChangedBy", await sql.AdminScalarAsync<string>(db, """
            SELECT STRING_AGG(name, ',') WITHIN GROUP (ORDER BY name) FROM sys.default_constraints
            WHERE name IN (N'DF_Ci_ChangedBy', N'DF_Relationship_ChangedBy')
                AND definition = N'(CONVERT([nvarchar](256),session_context(N''Goldmark.ChangedBy'')))';
            """));
        Assert.Equal("AuditLog.IX_AuditLog_Target,CiHistory.IX_CiHistory_Id,RelationshipHistory.IX_RelationshipHistory_FromCi,RelationshipHistory.IX_RelationshipHistory_ToCi",
            await sql.AdminScalarAsync<string>(db, """
                SELECT STRING_AGG(CONCAT(OBJECT_NAME(object_id), '.', name), ',') WITHIN GROUP (ORDER BY OBJECT_NAME(object_id), name) FROM sys.indexes
                WHERE name IN (N'IX_CiHistory_Id', N'IX_RelationshipHistory_FromCi', N'IX_RelationshipHistory_ToCi', N'IX_AuditLog_Target');
                """));
        // Both tables are still versioned into the same history tables.
        Assert.Equal("Ci:CiHistory,Relationship:RelationshipHistory", await sql.AdminScalarAsync<string>(db, """
            SELECT STRING_AGG(CONCAT(name, ':', OBJECT_NAME(history_table_id)), ',') WITHIN GROUP (ORDER BY name)
            FROM sys.tables WHERE temporal_type = 2 AND name IN (N'Ci', N'Relationship');
            """));
    }

    [Fact]
    public async Task Every_change_through_the_API_records_the_signed_in_person_and_the_audit_entry_names_the_same_one()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, adminPerson) = await scenario.SetUpAsync();
        var (editor, editorPerson) = await scenario.InviteAndAcceptAsync(admin, "eli.editor", "Editor");
        var (other, otherPerson) = await scenario.InviteAndAcceptAsync(admin, "ola.editor", "Editor");
        var db = scenario.Installation.Database;

        var created = await editor.PostAsync("/api/v1/cis", new { type = "Server", name = "srv-01" });
        Assert.True(created.Status == HttpStatusCode.Created, created.ToString());
        var id = created.Body.GetProperty("id").GetGuid();
        var host = await editor.PostAsync("/api/v1/cis", new { type = "HypervisorHost", name = "esx-01" });
        Assert.True(host.Status == HttpStatusCode.Created, host.ToString());
        Assert.Equal(editorPerson.Id.ToString("D"), await ChangedByAsync(scenario, id));

        // Another Editor's change, on a connection that may have just served the first one. (An Admin's deletion of
        // an attribute, which writes a version of every CI that had a value, is in TypeApiTests.)
        var read = AuthScenario.Ok(await other.GetAsync($"/api/v1/cis/{id}"));
        var changed = await other.PutAsync($"/api/v1/cis/{id}", new { name = "srv-01b", attributes = new { } }, read.ETag);
        Assert.True(changed.Status == HttpStatusCode.OK, changed.ToString());
        Assert.Equal(otherPerson.Id.ToString("D"), await ChangedByAsync(scenario, id));
        Assert.Equal(otherPerson.Id.ToString("D"), await sql.AdminScalarAsync<string>(db,
            $"SELECT TOP (1) ActorId FROM cmdb.AuditLog WHERE Action = 'ci.changed' AND TargetId = '{id:D}' ORDER BY Id DESC;"));

        // A relationship, and then its redundancy group by the other Editor.
        var added = await editor.PostAsync("/api/v1/relationships", new { type = "depends_on", from = id, to = host.Body.GetProperty("id").GetGuid(), redundancyGroup = "" });
        Assert.True(added.Status == HttpStatusCode.Created, added.ToString());
        Assert.Equal(editorPerson.Id.ToString("D"), await sql.AdminScalarAsync<string>(db, $"SELECT ChangedBy FROM cmdb.Relationship WHERE FromCiId = '{id:D}';"));
        Assert.NotEqual(adminPerson.Id, editorPerson.Id);
    }

    private static Task<T?> One<T>(DbSession db, [ConstantExpected] string sql, object? parameters = null) =>
        db.QuerySingleOrDefaultAsync<T>(sql, parameters, TestContext.Current.CancellationToken);

    private static Task<int> Run(DbSession db, [ConstantExpected] string sql, object? parameters = null) =>
        db.ExecuteAsync(sql, parameters, TestContext.Current.CancellationToken);

    private static Task<string> ChangedByAsync(AuthScenario scenario, Guid id) =>
        scenario.Sql.AdminScalarAsync<string>(scenario.Installation.Database, $"SELECT ChangedBy FROM cmdb.Ci WHERE Id = '{id:D}';");

    private static ActingPrincipal Acting(Guid user) => new(new HttpContextAccessor
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.ToString("D"))], AuthSchemes.Session)),
        },
    });
}

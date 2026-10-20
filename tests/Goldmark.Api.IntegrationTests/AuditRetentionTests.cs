using Goldmark.Api.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Goldmark.Api.IntegrationTests;

/// <summary>
/// Audit retention (NFR-LOG-3, DEC-041), measured against SQL Server 2025: the runtime login deletes old
/// entries only through <c>cmdb.PurgeAuditLog</c>, by ownership chaining, while its own DELETE is still
/// refused; the procedure keeps the 365-day floor even when the period row says less; each deletion
/// commits with its audit entry; <c>migrate</c> writes the configured period and audits a change.
/// </summary>
[Collection(SqlServerTests.Name)]
public sealed class AuditRetentionTests(SqlServerFixture sql)
{
    private const string Purge = "EXEC cmdb.PurgeAuditLog;";

    [Fact]
    public async Task The_purge_deletes_old_entries_through_the_procedure_while_a_direct_delete_is_refused()
    {
        var (installation, _) = await sql.ProvisionAsync();
        await PlantAsync(installation, ("it.old", -400), ("it.old", -366), ("it.young", -364), ("it.young", 0));

        var direct = await Assert.ThrowsAsync<SqlException>(() => AsRuntimeAsync(installation, "DELETE FROM cmdb.AuditLog WHERE Action = 'it.old';"));
        Assert.Equal(229, direct.Number); // permission denied: the DENY still holds
        Assert.Equal(2, await CountAsync(installation, "Action = 'it.old'"));

        Assert.Equal(2, await AsRuntimeScalarAsync<int>(installation, Purge));

        Assert.Equal(0, await CountAsync(installation, "Action = 'it.old'"));
        Assert.Equal(2, await CountAsync(installation, "Action = 'it.young'"));
        Assert.Equal(1, await CountAsync(installation, """
            Action = 'audit.retention.purged' AND ActorType = 'System' AND ActorId IS NULL
                AND JSON_VALUE(Details, '$.deleted') = '2' AND JSON_VALUE(Details, '$.retentionDays') = '365'
                AND JSON_VALUE(Details, '$.before') = CONVERT(varchar(19), DATEADD(day, -365, CAST(CAST(SYSUTCDATETIME() AS date) AS datetime2(0))), 126) + 'Z'
            """));

        // Nothing left to delete: the run writes nothing.
        Assert.Equal(0, await AsRuntimeScalarAsync<int>(installation, Purge));
        Assert.Equal(1, await CountAsync(installation, "Action = 'audit.retention.purged'"));
    }

    [Fact]
    public async Task The_procedure_keeps_the_365_day_floor_even_when_the_period_row_says_less()
    {
        var (installation, _) = await sql.ProvisionAsync();
        // Only by bypassing the CHECK constraint, as an administrator, can the row hold 30 days.
        await sql.AsAdminAsync(installation.Database, """
            ALTER TABLE cmdb.AuditRetention NOCHECK CONSTRAINT CK_AuditRetention_Days;
            UPDATE cmdb.AuditRetention SET RetentionDays = 30;
            """);
        await PlantAsync(installation, ("it.old", -400), ("it.young", -100));

        Assert.Equal(1, await AsRuntimeScalarAsync<int>(installation, Purge));

        Assert.Equal(0, await CountAsync(installation, "Action = 'it.old'"));
        Assert.Equal(1, await CountAsync(installation, "Action = 'it.young'"));
        Assert.Equal(1, await CountAsync(installation, "Action = 'audit.retention.purged' AND JSON_VALUE(Details, '$.retentionDays') = '365'"));
    }

    [Fact]
    public async Task The_purge_refuses_to_run_inside_a_callers_transaction()
    {
        var (installation, _) = await sql.ProvisionAsync();
        await PlantAsync(installation, ("it.old", -400));

        var refused = await Assert.ThrowsAsync<SqlException>(() => AsRuntimeAsync(installation, "BEGIN TRANSACTION; " + Purge));

        Assert.Equal(50041, refused.Number);
        Assert.Equal(1, await CountAsync(installation, "Action = 'it.old'"));
    }

    [Fact]
    public async Task Migrate_writes_the_configured_period_and_audits_each_change()
    {
        var (installation, provisioned) = await sql.ProvisionAsync();
        Assert.Contains("migrate: audit retention 365 days", provisioned.Output, StringComparison.Ordinal);
        Assert.Equal(365, await PeriodAsync(installation));
        Assert.Equal(0, await CountAsync(installation, "Action = 'audit.retention.changed'"));

        var longer = await sql.RunAsync(installation, ["migrate"], Retention("730"));
        Assert.True(longer.Code == 0, longer.ToString());
        Assert.Contains("migrate: audit retention changed to 730 days (was 365 days)", longer.Output, StringComparison.Ordinal);
        Assert.Equal(730, await PeriodAsync(installation));
        Assert.Equal(1, await CountAsync(installation, """
            Action = 'audit.retention.changed' AND ActorType = 'System'
                AND JSON_VALUE(Details, '$.before') = '365' AND JSON_VALUE(Details, '$.after') = '730'
            """));

        // The same value again changes nothing and writes nothing.
        var same = await sql.RunAsync(installation, ["migrate"], Retention("730"));
        Assert.Contains("migrate: audit retention 730 days", same.Output, StringComparison.Ordinal);
        Assert.Equal(1, await CountAsync(installation, "Action = 'audit.retention.changed'"));

        // 0 keeps entries indefinitely: the purge deletes nothing.
        Assert.Equal(0, (await sql.RunAsync(installation, ["migrate"], Retention("0"))).Code);
        Assert.Equal(0, await PeriodAsync(installation));
        await PlantAsync(installation, ("it.old", -4000));
        Assert.Equal(0, await AsRuntimeScalarAsync<int>(installation, Purge));
        Assert.Equal(1, await CountAsync(installation, "Action = 'it.old'"));

        // Without the setting, the default comes back, and that is audited too.
        Assert.Equal(0, (await sql.RunAsync(installation, ["migrate"])).Code);
        Assert.Equal(365, await PeriodAsync(installation));
        Assert.Equal(3, await CountAsync(installation, "Action = 'audit.retention.changed'"));
    }

    [Theory]
    [InlineData("364")]
    [InlineData("-1")]
    [InlineData("36501")]
    [InlineData("a year")]
    public async Task Migrate_refuses_a_period_under_the_floor_or_not_a_number_and_changes_nothing(string value)
    {
        var (installation, _) = await sql.ProvisionAsync();

        var refused = await sql.RunAsync(installation, ["migrate"], Retention(value));

        Assert.Equal(2, refused.Code);
        Assert.Contains("Goldmark:Audit:RetentionDays", refused.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(value, refused.Error, StringComparison.Ordinal); // the setting's name, never its value
        Assert.Equal(365, await PeriodAsync(installation));
    }

    private static Dictionary<string, string?> Retention(string days) => new() { ["Goldmark:Audit:RetentionDays"] = days };

    /// <summary>Audit entries with the given action, dated the given number of days from now (negative: in the past).</summary>
    private Task PlantAsync(Installation installation, params (string Action, int Days)[] entries) =>
        sql.AsAdminAsync(installation.Database, "INSERT INTO cmdb.AuditLog (At, ActorType, Action) VALUES " + string.Join(", ",
            entries.Select(e => $"(DATEADD(day, {e.Days}, SYSUTCDATETIME()), 'System', '{e.Action}')")) + ";");

    private Task<int> CountAsync(Installation installation, string where) =>
        sql.AdminScalarAsync<int>(installation.Database, $"SELECT COUNT(*) FROM cmdb.AuditLog WHERE {where};");

    private Task<int> PeriodAsync(Installation installation) =>
        sql.AdminScalarAsync<int>(installation.Database, "SELECT RetentionDays FROM cmdb.AuditRetention WHERE Id = 1;");

    private Task AsRuntimeAsync(Installation installation, string statement) =>
        sql.ExecuteAsync(installation.RuntimeUser, installation.RuntimePassword, installation.Database, statement);

    private Task<T> AsRuntimeScalarAsync<T>(Installation installation, string statement) =>
        sql.ScalarAsync<T>(installation.RuntimeUser, installation.RuntimePassword, installation.Database, statement);
}

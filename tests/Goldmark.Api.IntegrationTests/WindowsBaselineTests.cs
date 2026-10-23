using Goldmark.Api.Commands;
using Microsoft.Data.SqlClient;

namespace Goldmark.Api.IntegrationTests;

/// <summary>
/// The grants SQL Server 2025 on Windows creates beyond the Linux baseline (DEC-043 item 12): measured on a fresh
/// instance by the Windows CI job, which names it in <c>GOLDMARK_WINDOWS_SQL_SERVER</c> (and its pinned certificate
/// in <c>GOLDMARK_WINDOWS_SQL_CERTIFICATE</c>); skipped elsewhere. On a difference it fails with the measured lines,
/// for sql-server-2025-public-grants.windows.txt.
/// </summary>
public sealed class WindowsBaselineTests
{
    [Fact]
    public async Task The_Windows_additions_match_a_fresh_SQL_Server_2025_on_Windows()
    {
        var server = Environment.GetEnvironmentVariable("GOLDMARK_WINDOWS_SQL_SERVER");
        if (string.IsNullOrEmpty(server))
        {
            Assert.Skip("Set GOLDMARK_WINDOWS_SQL_SERVER to a fresh Windows SQL Server 2025 (the Windows CI job does).");
        }

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = $"tcp:{server},1433",
            InitialCatalog = "master",
            IntegratedSecurity = true,
            Encrypt = SqlConnectionEncryptOption.Strict,
            ServerCertificate = Environment.GetEnvironmentVariable("GOLDMARK_WINDOWS_SQL_CERTIFICATE"),
        };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        Assert.True(await PrincipalAudit.IsWindowsAsync(connection));

        var baseline = PrincipalAudit.Baseline.Value;
        var lines = new List<string>();
        foreach (var database in PrincipalAudit.SystemDatabases)
        {
            await connection.ChangeDatabaseAsync(database, TestContext.Current.CancellationToken);
            foreach (var grant in await PrincipalAudit.PublicGrantsAsync(connection))
            {
                var key = PrincipalAudit.BaselineKey(grant.Grantee, grant.Permission, grant.Class, grant.Target);
                if (!baseline["*"].Contains(key) && !(baseline.TryGetValue(database, out var own) && own.Contains(key)))
                {
                    lines.Add($"{(database == "model" ? "*" : database)}|{key}");
                }
            }
        }

        var expected = PrincipalAudit.WindowsBaseline.Value.SelectMany(scope => scope.Value.Select(k => $"{scope.Key}|{k}")).ToHashSet(StringComparer.Ordinal);
        Assert.True(expected.SetEquals(lines),
            "The Windows additions differ from sql-server-2025-public-grants.windows.txt. Measured lines:" + Environment.NewLine +
            string.Join(Environment.NewLine, lines.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)));
    }
}

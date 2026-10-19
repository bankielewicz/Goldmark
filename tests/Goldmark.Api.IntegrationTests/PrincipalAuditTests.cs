using System.Globalization;
using Goldmark.Api.Commands;
using Goldmark.Api.IntegrationTests.Infrastructure;

namespace Goldmark.Api.IntegrationTests;

/// <summary>
/// The principal audit of Goldmark's own logins (#4, DEC-031): an adopted login is audited before
/// anything is changed, and a managed one is revalidated on every run. Findings about Goldmark's
/// principals refuse (exit 3) and cannot be accepted.
/// </summary>
[Collection(SqlServerTests.Name)]
public sealed class PrincipalAuditTests(SqlServerFixture sql)
{
    private static readonly Dictionary<string, string?> Adopt = new() { ["Goldmark:Bootstrap:AdoptExistingLogin"] = "true" };

    private static string Format(string template, Installation installation, string password, int n) =>
        string.Format(CultureInfo.InvariantCulture, template, installation.RuntimeUser, password, n, installation.Database);

    [Theory]
    [InlineData("CREATE LOGIN {0} WITH PASSWORD = N'{1}', CHECK_POLICY = OFF;", "login-policy-off:{0}")]
    [InlineData("CREATE LOGIN {0} WITH PASSWORD = N'{1}', CHECK_EXPIRATION = ON;", "login-expiration-on:{0}")]
    [InlineData("CREATE LOGIN {0} WITH PASSWORD = N'{1}' MUST_CHANGE, CHECK_EXPIRATION = ON;", "login-must-change:{0}")]
    [InlineData("CREATE LOGIN {0} WITH PASSWORD = N'{1}'; ALTER LOGIN {0} DISABLE;", "login-disabled:{0}")]
    [InlineData("CREATE LOGIN {0} WITH PASSWORD = N'{1}'; DENY CONNECT SQL TO {0};", "login-connect-denied:{0}:{0}")]
    [InlineData("CREATE CREDENTIAL ItCred{2} WITH IDENTITY = N'it', SECRET = N'it-secret'; CREATE LOGIN {0} WITH PASSWORD = N'{1}'; ALTER LOGIN {0} WITH CREDENTIAL = ItCred{2};",
        "login-credential:{0}:ItCred{2}")]
    [InlineData("CREATE LOGIN {0} WITH PASSWORD = N'{1}'; GRANT IMPERSONATE ON LOGIN::sa TO {0};", "server-grant:{0}:IMPERSONATE:LOGIN:[sa]")]
    [InlineData("CREATE LOGIN {0} WITH PASSWORD = N'{1}'; GRANT VIEW SERVER STATE TO {0};", "server-grant:{0}:VIEW+SERVER+STATE:SERVER")]
    [InlineData("CREATE LOGIN {0} WITH PASSWORD = N'{1}'; ALTER SERVER ROLE bulkadmin ADD MEMBER {0};", "server-role:{0}:bulkadmin")]
    [InlineData("CREATE LOGIN {0} WITH PASSWORD = N'{1}'; CREATE DATABASE ItMapped{2}; EXEC ItMapped{2}.sys.sp_executesql N'CREATE USER {0} FOR LOGIN {0};';",
        "mapped-into:{0}:ItMapped{2}")]
    [InlineData("CREATE LOGIN {0} WITH PASSWORD = N'{1}'; CREATE DATABASE ItOwned{2}; ALTER AUTHORIZATION ON DATABASE::ItOwned{2} TO {0};",
        "owns:{0}:database:ItOwned{2}")]
    [InlineData("CREATE LOGIN {0} WITH PASSWORD = N'{1}'; EXEC sp_addlinkedserver @server = N'ItLs{2}', @srvproduct = N'', @provider = N'MSOLEDBSQL', @datasrc = N'remote.example'; " +
        "EXEC sp_addlinkedsrvlogin @rmtsrvname = N'ItLs{2}', @useself = N'FALSE', @locallogin = N'{0}', @rmtuser = N'remote', @rmtpassword = N'not-a-secret-1A';",
        "linked-login:{0}:ItLs{2}")]
    public async Task An_adopted_login_is_audited_before_anything_is_changed(string setup, string expectedId) // #4
    {
        var installation = sql.NewInstallation();
        var foreign = SqlServerFixture.Secret();
        var n = int.Parse(installation.Database["GoldmarkIt".Length..], CultureInfo.InvariantCulture);
        await sql.AsAdminAsync("master", Format(setup, installation, foreign, n));

        var refused = await sql.RunAsync(installation, ["bootstrap-db", "migrate"], Adopt);

        Assert.Equal(3, refused.Code);
        Assert.Contains($"[{Format(expectedId, installation, foreign, n)}]", refused.Error, StringComparison.Ordinal);
        Assert.Contains("Nothing was changed.", refused.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("GOLDMARK_DB_ACCEPTED_FINDINGS", refused.Error, StringComparison.Ordinal);
        Assert.Equal(0, await sql.AdminScalarAsync<int>("master", $"SELECT COUNT(*) FROM sys.databases WHERE name = N'{installation.Database}';"));
        Assert.Equal(1, await sql.AdminScalarAsync<int>("master",
            $"SELECT PWDCOMPARE(N'{foreign}', password_hash) FROM sys.sql_logins WHERE name = N'{installation.RuntimeUser}';"));
        Assert.Equal(0, await sql.AdminScalarAsync<int>("master", $"SELECT COUNT(*) FROM sys.server_principals WHERE name = N'{installation.MigrationUser}';"));
    }

    [Theory]
    [InlineData("ALTER ROLE db_datareader ADD MEMBER {0};", "database-role:{0}:db_datareader")]
    [InlineData("CREATE USER it_other_user WITHOUT LOGIN; GRANT IMPERSONATE ON USER::it_other_user TO {0};", "database-grant:{0}:IMPERSONATE:USER:[it_other_user]")]
    [InlineData("GRANT UPDATE ON OBJECT::cmdb.AuditLog TO {0};", "database-grant:{0}:UPDATE:OBJECT:[cmdb].[AuditLog]")] // direct, even though the role's DENY wins
    [InlineData("DENY CONNECT TO {0};", "database-connect-denied:{0}:{0}")]
    [InlineData("EXEC('CREATE SCHEMA it_owned'); ALTER AUTHORIZATION ON SCHEMA::it_owned TO {0};", "owns:{0}:schema:it_owned")]
    [InlineData("EXEC('CREATE SCHEMA it_other'); GRANT SELECT ON SCHEMA::it_other TO goldmark_runtime;", "database-securable:{0}:SCHEMA:it_other:SELECT")]
    [InlineData("GRANT VIEW DEFINITION ON SCHEMA::dbo TO goldmark_runtime;", "database-securable:{0}:OBJECT:dbo.SchemaVersions:VIEW+DEFINITION")]
    [InlineData("EXEC('CREATE PROCEDURE dbo.ItProc AS SELECT 1;'); GRANT EXECUTE ON dbo.ItProc TO goldmark_runtime;", "database-securable:{0}:OBJECT:dbo.ItProc:EXECUTE")]
    [InlineData("GRANT REFERENCES ON OBJECT::cmdb.Ci TO goldmark_runtime;", "database-securable:{0}:OBJECT:cmdb.Ci:REFERENCES")]
    [InlineData("CREATE TYPE dbo.ItType FROM int; GRANT REFERENCES ON TYPE::dbo.ItType TO goldmark_runtime;", "database-securable:{0}:TYPE:dbo.ItType:REFERENCES")]
    [InlineData("CREATE CERTIFICATE ItCert ENCRYPTION BY PASSWORD = N'Cert-Pa55word!' WITH SUBJECT = N'it'; GRANT CONTROL ON CERTIFICATE::ItCert TO goldmark_runtime;",
        "database-securable:{0}:CERTIFICATE:ItCert:CONTROL")]
    [InlineData("CREATE USER it_imp WITHOUT LOGIN; GRANT IMPERSONATE ON USER::it_imp TO goldmark_runtime;", "database-securable:{0}:USER:it_imp:IMPERSONATE")]
    [InlineData("GRANT SELECT ON SCHEMA::cmdb TO public;", "goldmark-grant:{3}:public:SELECT:SCHEMA:[cmdb]")]
    [InlineData("ALTER DATABASE CURRENT SET TRUSTWORTHY ON;", "goldmark-database-option:{3}:TRUSTWORTHY")]
    public async Task A_managed_runtime_login_changed_out_of_band_is_refused_on_the_next_run(string change, string expectedId) // #4
    {
        var (installation, _) = await sql.ProvisionAsync();
        await sql.AsAdminAsync(installation.Database, Format(change, installation, "", 0));

        var refused = await sql.RunAsync(installation, ["bootstrap-db"]);

        Assert.Equal(3, refused.Code);
        Assert.Contains($"[{Format(expectedId, installation, "", 0)}]", refused.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("GOLDMARK_DB_ACCEPTED_FINDINGS", refused.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_adopted_same_name_user_is_audited_before_it_is_remapped() // Codex on #17
    {
        var installation = sql.NewInstallation();
        await sql.AsAdminAsync("master", $"CREATE DATABASE {installation.Database};");
        await sql.AsAdminAsync(installation.Database,
            $"CREATE USER {installation.RuntimeUser} WITHOUT LOGIN; ALTER ROLE db_owner ADD MEMBER {installation.RuntimeUser};");

        var refused = await sql.RunAsync(installation, ["bootstrap-db", "migrate"], Adopt);

        Assert.Equal(3, refused.Code);
        Assert.Contains($"[database-role:{installation.RuntimeUser}:db_owner]", refused.Error, StringComparison.Ordinal);
        Assert.Contains("Nothing was changed.", refused.Error, StringComparison.Ordinal);
        Assert.Equal(0, await sql.AdminScalarAsync<int>("master",
            $"SELECT COUNT(*) FROM sys.server_principals WHERE name IN (N'{installation.RuntimeUser}', N'{installation.MigrationUser}');"));
    }

    [Fact]
    public async Task A_Goldmark_database_owned_by_a_login_without_CONTROL_SERVER_is_refused() // Codex security review on #17
    {
        // A login allowed to create databases creates "Goldmark" first and stays its dbo.
        var installation = sql.NewInstallation();
        var creator = $"{installation.MigrationUser}_creator";
        var creatorPassword = SqlServerFixture.Secret();
        await sql.AsAdminAsync("master", $"CREATE LOGIN {creator} WITH PASSWORD = N'{creatorPassword}'; ALTER SERVER ROLE dbcreator ADD MEMBER {creator};");
        await sql.ExecuteAsync(creator, creatorPassword, "master", $"CREATE DATABASE {installation.Database};");

        var refused = await sql.RunAsync(installation, ["bootstrap-db", "migrate"]);

        Assert.Equal(3, refused.Code);
        Assert.Contains($"[goldmark-database-owner:{installation.Database}:{creator}]", refused.Error, StringComparison.Ordinal);
        Assert.Contains("Nothing was changed.", refused.Error, StringComparison.Ordinal);
        Assert.Equal(0, await sql.AdminScalarAsync<int>("master",
            $"SELECT COUNT(*) FROM sys.server_principals WHERE name IN (N'{installation.RuntimeUser}', N'{installation.MigrationUser}');"));

        await sql.AsAdminAsync("master", $"ALTER AUTHORIZATION ON DATABASE::{installation.Database} TO sa;");
        var owned = await sql.RunAsync(installation, ["bootstrap-db", "migrate"]);
        Assert.True(owned.Code == 0, owned.ToString());
    }

    [Fact]
    public async Task Another_principal_with_rights_in_the_Goldmark_database_is_refused() // Codex security review on #17
    {
        var (installation, _) = await sql.ProvisionAsync();
        await sql.AsAdminAsync(installation.Database, "CREATE USER it_extra WITHOUT LOGIN; ALTER ROLE db_datareader ADD MEMBER it_extra;");

        var refused = await sql.RunAsync(installation, ["bootstrap-db"]);

        Assert.Equal(3, refused.Code);
        Assert.Contains($"[goldmark-database-principal:{installation.Database}:it_extra]", refused.Error, StringComparison.Ordinal);

        await sql.AsAdminAsync(installation.Database, "ALTER ROLE db_datareader DROP MEMBER it_extra;");
        Assert.Equal(0, (await sql.RunAsync(installation, ["bootstrap-db"])).Code); // CONNECT alone is not a finding
    }

    [Fact]
    public async Task Objects_owned_by_the_migration_login_are_refused() // #4, DEC-029
    {
        var (installation, _) = await sql.ProvisionAsync();
        await sql.AsAdminAsync(installation.Database, $"ALTER AUTHORIZATION ON SCHEMA::cmdb TO {installation.MigrationUser};");

        var refused = await sql.RunAsync(installation, ["bootstrap-db"]);

        Assert.Equal(3, refused.Code);
        Assert.Contains($"[owns:{installation.MigrationUser}:schema:cmdb]", refused.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_freshly_provisioned_installation_passes_the_audit()
    {
        var (installation, result) = await sql.SharedAsync("principal-audit");

        Assert.Contains("bootstrap-db: principal audit passed for both logins and the instance.", result.Output, StringComparison.Ordinal);
        Assert.Equal(0, await sql.AdminScalarAsync<int>("master", $"SELECT CAST(is_auto_close_on AS int) FROM sys.databases WHERE name = N'{installation.Database}';"));

        var again = await sql.RunAsync(installation, ["bootstrap-db", "migrate"]);
        Assert.True(again.Code == 0, again.ToString());
    }

    [Fact]
    public async Task A_login_mapped_to_a_differently_named_user_is_refused_with_the_rename_to_run() // #15
    {
        var installation = sql.NewInstallation();
        await sql.AsAdminAsync("master", $"CREATE DATABASE {installation.Database}; CREATE LOGIN {installation.RuntimeUser} WITH PASSWORD = N'{installation.RuntimePassword}';");
        await sql.AsAdminAsync(installation.Database, $"CREATE USER it_other_name FOR LOGIN {installation.RuntimeUser};");

        var refused = await sql.RunAsync(installation, ["bootstrap-db", "migrate"], Adopt);

        Assert.Equal(3, refused.Code);
        Assert.Contains($"is named 'it_other_name'; Goldmark requires it to have the login's name. Nothing was changed. " +
            $"Fix: USE [{installation.Database}]; ALTER USER [it_other_name] WITH NAME = [{installation.RuntimeUser}];", refused.Error, StringComparison.Ordinal);
        Assert.Equal(0, await sql.AdminScalarAsync<int>(installation.Database, "SELECT COUNT(*) FROM sys.extended_properties WHERE name = N'goldmark:managed';"));

        await sql.AsAdminAsync(installation.Database, $"ALTER USER it_other_name WITH NAME = {installation.RuntimeUser};");
        var adopted = await sql.RunAsync(installation, ["bootstrap-db", "migrate"], Adopt);

        Assert.True(adopted.Code == 0, adopted.ToString());
        Assert.Equal(1, await sql.ScalarAsync<int>(installation.RuntimeUser, installation.RuntimePassword, installation.Database, "SELECT IS_ROLEMEMBER('goldmark_runtime');"));
    }

    [Fact]
    public async Task A_managed_user_renamed_out_of_band_is_refused() // #15
    {
        var (installation, _) = await sql.ProvisionAsync();
        await sql.AsAdminAsync(installation.Database, $"ALTER USER {installation.RuntimeUser} WITH NAME = it_renamed;");

        var refused = await sql.RunAsync(installation, ["bootstrap-db", "migrate"]);

        Assert.Equal(3, refused.Code);
        Assert.Contains("ALTER USER [it_renamed] WITH NAME", refused.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_system_database_baseline_matches_the_pinned_SQL_Server_image()
    {
        // When this fails after a SQL Server image update, replace
        // src/Goldmark.Api/Commands/Audit/sql-server-2025-public-grants.txt with the measured file it names.
        var baseline = PrincipalAudit.Baseline.Value;
        var measured = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        await using (var connection = await sql.OpenAsync("sa", sql.SaPassword, "master"))
        {
            foreach (var database in PrincipalAudit.SystemDatabases)
            {
                await connection.ChangeDatabaseAsync(database, TestContext.Current.CancellationToken);
                measured[database] = [.. (await PrincipalAudit.PublicGrantsAsync(connection)).Select(g => PrincipalAudit.BaselineKey(g.Grantee, g.Permission, g.Class, g.Target))];
            }
        }

        var lines = new List<string>();
        lines.AddRange(measured["model"].Order(StringComparer.Ordinal).Select(k => $"*|{k}"));
        foreach (var database in PrincipalAudit.SystemDatabases.Where(d => d != "model"))
        {
            lines.AddRange(measured[database].Except(measured["model"]).Order(StringComparer.Ordinal).Select(k => $"{database}|{k}"));
        }

        var expected = baseline.SelectMany(scope => scope.Value.Select(k => $"{scope.Key}|{k}")).ToHashSet(StringComparer.Ordinal);
        if (!expected.SetEquals(lines))
        {
            var path = Path.Combine(Path.GetTempPath(), "sql-server-2025-public-grants.measured.txt");
            await File.WriteAllLinesAsync(path, lines, TestContext.Current.CancellationToken);
            Assert.Fail($"The measured public/guest grants differ from the embedded baseline: {expected.Except(lines).Count()} missing, " +
                $"{lines.Except(expected).Count()} new. Measured lines (without the header): {path}");
        }

        // A new database copies model, so a fresh Goldmark database has exactly the "*" set.
        var (installation, _) = await sql.SharedAsync("principal-audit");
        await using var goldmark = await sql.OpenAsync("sa", sql.SaPassword, installation.Database);
        var own = (await PrincipalAudit.PublicGrantsAsync(goldmark)).Select(g => PrincipalAudit.BaselineKey(g.Grantee, g.Permission, g.Class, g.Target)).ToHashSet(StringComparer.Ordinal);
        Assert.True(own.SetEquals(baseline["*"]), string.Join(", ", own.Except(baseline["*"]).Concat(baseline["*"].Except(own))));
    }
}

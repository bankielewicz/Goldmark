using Goldmark.Api.IntegrationTests.Infrastructure;

namespace Goldmark.Api.IntegrationTests;

/// <summary>A second SQL Server container for tests that change instance-wide state (they run one at a time).</summary>
[CollectionDefinition(Name)]
public sealed class SharedInstanceTests : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SQL Server 2025, instance-wide audit";
}

/// <summary>
/// The principal audit's checks of the shared instance (#2, #3, #4; DEC-031): conditions that give
/// every login, Goldmark's included, rights it was never granted. Each is refused before anything
/// is created, can be accepted by its ID, and passes once fixed.
/// </summary>
[Collection(SharedInstanceTests.Name)]
public sealed class InstanceAuditTests(SqlServerFixture sql)
{
    private static Dictionary<string, string?> Accept(string id) => new() { ["Goldmark:Bootstrap:AcceptedFindings"] = id };

    /// <summary>Applies <paramref name="change"/>, expects a refusal naming <paramref name="id"/>, then acceptance, then a pass after <paramref name="undo"/>.</summary>
    private async Task RefusedAcceptedAndFixedAsync(string change, string undo, string id, string expected)
    {
        var installation = sql.NewInstallation();
        await sql.AsAdminAsync("master", change);
        try
        {
            var refused = await sql.RunAsync(installation, ["bootstrap-db", "migrate"]);

            Assert.True(refused.Code == 3 && refused.Error.Contains($"[{id}]", StringComparison.Ordinal), refused.ToString());
            Assert.Contains(expected, refused.Error, StringComparison.Ordinal);
            Assert.Contains($"add {id} to GOLDMARK_DB_ACCEPTED_FINDINGS", refused.Error, StringComparison.Ordinal);
            Assert.Contains("Nothing was changed.", refused.Error, StringComparison.Ordinal);
            Assert.Equal(0, await sql.AdminScalarAsync<int>("master", $"SELECT COUNT(*) FROM sys.databases WHERE name = N'{installation.Database}';"));
            Assert.Equal(0, await sql.AdminScalarAsync<int>("master",
                $"SELECT COUNT(*) FROM sys.server_principals WHERE name IN (N'{installation.RuntimeUser}', N'{installation.MigrationUser}');"));

            var accepted = await sql.RunAsync(installation, ["bootstrap-db", "migrate"], Accept(id));

            Assert.True(accepted.Code == 0, accepted.ToString());
            Assert.Contains($"bootstrap-db: accepted finding [{id}]", accepted.Output, StringComparison.Ordinal);
        }
        finally
        {
            await sql.AsAdminAsync("master", undo);
        }

        var fixedRun = await sql.RunAsync(installation, ["bootstrap-db", "migrate"], Accept(id));
        Assert.True(fixedRun.Code == 0, fixedRun.ToString());
        Assert.Contains($"warning: accepted finding [{id}] did not occur", fixedRun.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Guest_access_to_another_database_is_refused_until_revoked_or_accepted() // #2
    {
        await sql.AsAdminAsync("master", "CREATE DATABASE ItGuestOpen;");
        await RefusedAcceptedAndFixedAsync(
            "EXEC ItGuestOpen.sys.sp_executesql N'GRANT CONNECT TO guest;';",
            "EXEC ItGuestOpen.sys.sp_executesql N'REVOKE CONNECT FROM guest;';",
            "guest-connect:ItGuestOpen",
            "guest can connect to database 'ItGuestOpen', so every login, Goldmark's included, can enter it. Fix: USE [ItGuestOpen]; REVOKE CONNECT FROM guest;");
    }

    [Fact]
    public async Task Guest_access_is_also_found_from_the_logins_own_rights() // #2, effective layer
    {
        await sql.AsAdminAsync("master", "CREATE DATABASE ItGuestLate;");
        var (installation, _) = await sql.ProvisionAsync();
        await sql.AsAdminAsync("master", "EXEC ItGuestLate.sys.sp_executesql N'GRANT CONNECT TO guest;';");
        try
        {
            await using var connection = await sql.OpenAsync(installation.RuntimeUser, installation.RuntimePassword, installation.Database);

            var findings = await Commands.PrincipalAudit.SelfAsync(connection, installation.Database, runtime: true);

            Assert.Contains(findings, f => f.Id == "guest-connect:ItGuestLate");
            Assert.DoesNotContain(findings, f => f.Id.EndsWith(":master", StringComparison.Ordinal) || f.Id.EndsWith(":msdb", StringComparison.Ordinal));
        }
        finally
        {
            await sql.AsAdminAsync("master", "EXEC ItGuestLate.sys.sp_executesql N'REVOKE CONNECT FROM guest;';");
        }
    }

    [Fact]
    public async Task Guest_access_in_model_is_refused_and_cannot_be_accepted_for_the_Goldmark_database_it_would_be_copied_into() // #2
    {
        var installation = sql.NewInstallation();
        await sql.AsAdminAsync("master", "EXEC model.sys.sp_executesql N'GRANT CONNECT TO guest;';");
        try
        {
            var refused = await sql.RunAsync(installation, ["bootstrap-db"]);
            Assert.Equal(3, refused.Code);
            Assert.Contains("[guest-connect:model]", refused.Error, StringComparison.Ordinal);

            // A new database copies model, including guest's CONNECT: the Goldmark database itself is then open.
            var accepted = await sql.RunAsync(installation, ["bootstrap-db"], Accept("guest-connect:model"));

            Assert.True(accepted.Code == 3 && accepted.Error.Contains($"[goldmark-grant:{installation.Database}:guest:CONNECT]", StringComparison.Ordinal), accepted.ToString());
            Assert.Contains("The changes listed above were made", accepted.Error, StringComparison.Ordinal);
        }
        finally
        {
            await sql.AsAdminAsync("master", "EXEC model.sys.sp_executesql N'REVOKE CONNECT FROM guest;';");
        }

        await sql.AsAdminAsync(installation.Database, "REVOKE CONNECT FROM guest;");
        var fixedRun = await sql.RunAsync(installation, ["bootstrap-db", "migrate"]);
        Assert.True(fixedRun.Code == 0, fixedRun.ToString());
    }

    [Fact]
    public async Task Guest_access_to_the_Goldmark_database_itself_is_refused_and_cannot_be_accepted() // #2
    {
        var (installation, _) = await sql.ProvisionAsync();
        await sql.AsAdminAsync(installation.Database, "GRANT CONNECT TO guest;");
        try
        {
            var refused = await sql.RunAsync(installation, ["bootstrap-db"]);

            Assert.True(refused.Code == 3 && refused.Error.Contains($"[goldmark-grant:{installation.Database}:guest:CONNECT]", StringComparison.Ordinal), refused.ToString());
            Assert.DoesNotContain($"add goldmark-grant", refused.Error, StringComparison.Ordinal);
        }
        finally
        {
            await sql.AsAdminAsync(installation.Database, "REVOKE CONNECT FROM guest;");
        }
    }

    [Fact]
    public async Task A_wildcard_linked_server_mapping_to_a_remote_login_is_refused() // #3
    {
        await RefusedAcceptedAndFixedAsync(
            """
            EXEC sp_addlinkedserver @server = N'ItLinkedWild', @srvproduct = N'', @provider = N'MSOLEDBSQL', @datasrc = N'remote.example';
            EXEC sp_addlinkedsrvlogin @rmtsrvname = N'ItLinkedWild', @useself = N'FALSE', @locallogin = NULL, @rmtuser = N'remote_sa', @rmtpassword = N'not-a-secret-1A';
            """,
            "EXEC sp_dropserver @server = N'ItLinkedWild', @droplogins = 'droplogins';",
            "linked-login-all-logins:ItLinkedWild",
            "linked server 'ItLinkedWild' maps every login to a fixed remote login");
    }

    [Fact]
    public async Task The_default_self_credential_mapping_of_a_linked_server_passes() // #3
    {
        await sql.AsAdminAsync("master", "EXEC sp_addlinkedserver @server = N'ItLinkedSelf', @srvproduct = N'', @provider = N'MSOLEDBSQL', @datasrc = N'remote.example';");
        try
        {
            Assert.Equal(1, await sql.AdminScalarAsync<int>("master",
                "SELECT COUNT(*) FROM sys.linked_logins AS l JOIN sys.servers AS s ON s.server_id = l.server_id WHERE s.name = N'ItLinkedSelf' AND l.local_principal_id = 0 AND l.uses_self_credential = 1;"));

            var (_, result) = await sql.ProvisionAsync();

            Assert.DoesNotContain("ItLinkedSelf", result.Output + result.Error, StringComparison.Ordinal);
        }
        finally
        {
            await sql.AsAdminAsync("master", "EXEC sp_dropserver @server = N'ItLinkedSelf', @droplogins = 'droplogins';");
        }
    }

    [Fact]
    public async Task A_wildcard_remote_login_is_refused() // #3
    {
        // SQL Server 2025 no longer creates remote servers (error 15663), but a linked server with
        // RPC can still carry remote logins.
        await RefusedAcceptedAndFixedAsync(
            """
            EXEC sp_addlinkedserver @server = N'ItRemote', @srvproduct = N'', @provider = N'MSOLEDBSQL', @datasrc = N'remote.example';
            EXEC sp_serveroption N'ItRemote', N'rpc', N'true';
            EXEC sp_addremotelogin @remoteserver = N'ItRemote';
            """,
            "EXEC sp_dropserver @server = N'ItRemote', @droplogins = 'droplogins';",
            "remote-login-all-logins:ItRemote",
            "remote server 'ItRemote' accepts every login");
    }

    [Fact]
    public async Task A_server_level_grant_to_public_is_refused_and_cannot_be_accepted() // #4
    {
        // Every login inherits it, so it is a right of Goldmark's runtime login like any other (DEC-015).
        var installation = sql.NewInstallation();
        const string id = "public-server-grant:VIEW+SERVER+STATE:SERVER";
        await sql.AsAdminAsync("master", "GRANT VIEW SERVER STATE TO public;");
        try
        {
            var refused = await sql.RunAsync(installation, ["bootstrap-db"]);

            Assert.Equal(3, refused.Code);
            Assert.Contains($"[{id}] public holds VIEW SERVER STATE on the server, which every login inherits. Fix: REVOKE VIEW SERVER STATE FROM public;",
                refused.Error, StringComparison.Ordinal);
            Assert.DoesNotContain("GOLDMARK_DB_ACCEPTED_FINDINGS", refused.Error, StringComparison.Ordinal);
            Assert.Equal(2, (await sql.RunAsync(installation, ["bootstrap-db"], Accept(id))).Code);
        }
        finally
        {
            await sql.AsAdminAsync("master", "REVOKE VIEW SERVER STATE FROM public;");
        }

        Assert.Equal(0, (await sql.RunAsync(installation, ["bootstrap-db", "migrate"])).Code);
    }

    [Theory]
    [InlineData("msdb", "GRANT SELECT ON dbo.sysjobs TO public;", "REVOKE SELECT ON dbo.sysjobs FROM public;", "system-grant:msdb:public:SELECT:dbo.sysjobs")]
    [InlineData("master", "GRANT EXECUTE ON sys.xp_cmdshell TO public;", "REVOKE EXECUTE ON sys.xp_cmdshell FROM public;", "system-grant:master:public:EXECUTE:sys.xp_cmdshell")]
    [InlineData("tempdb", "GRANT VIEW DATABASE STATE TO public;", "REVOKE VIEW DATABASE STATE FROM public;", "system-grant:tempdb:public:VIEW+DATABASE+STATE")]
    public async Task Grants_to_public_or_guest_in_system_databases_beyond_the_measured_baseline_are_refused(
        string database, string grant, string revoke, string id) // #4
    {
        await RefusedAcceptedAndFixedAsync(
            $"EXEC {database}.sys.sp_executesql N'{grant}';",
            $"EXEC {database}.sys.sp_executesql N'{revoke}';",
            id,
            "beyond SQL Server 2025's default");
    }

    [Fact]
    public async Task Cross_database_ownership_chaining_is_refused() // #4, ownership chains
    {
        await RefusedAcceptedAndFixedAsync(
            "EXEC sp_configure 'cross db ownership chaining', 1; RECONFIGURE;",
            "EXEC sp_configure 'cross db ownership chaining', 0; RECONFIGURE;",
            "cross-db-chaining",
            "'cross db ownership chaining' is on");
    }

    [Fact]
    public async Task Findings_about_Goldmarks_own_principals_cannot_be_accepted()
    {
        var installation = sql.NewInstallation();

        var result = await sql.RunAsync(installation, ["bootstrap-db"], Accept("server-role:goldmark_app:sysadmin"));

        Assert.Equal(2, result.Code);
        Assert.Contains("findings of kind 'server-role' cannot be accepted; fix them instead", result.Error, StringComparison.Ordinal);
    }
}

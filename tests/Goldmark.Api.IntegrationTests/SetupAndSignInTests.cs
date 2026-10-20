using System.Net;
using Goldmark.Api.IntegrationTests.Infrastructure;

namespace Goldmark.Api.IntegrationTests;

/// <summary>
/// First-run setup (US-02) and sign-in with password and second factor (US-03) against real SQL
/// Server 2025, through the API as the SPA uses it.
/// </summary>
[Collection(SqlServerTests.Name)]
public sealed class SetupAndSignInTests(SqlServerFixture sql)
{
    [Fact]
    public async Task Setup_creates_the_first_administrator_with_two_factor_then_closes_for_good()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var token = await scenario.App.SetupTokenAsync();
        var client = scenario.App.Client();

        Assert.Equal("required", AuthScenario.Ok(await client.GetAsync("/api/v1/setup")).Value("status"));
        Assert.Equal("invalid-setup-token", (await client.PostAsync("/api/v1/setup/token", new { token = "AAAA-BBBB" })).Code);
        Assert.Single(await scenario.AuditAsync("setup.token-rejected"));
        // Nothing else is reachable with only a token: no enrolment cookie yet.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/enrolment")).Status);

        AuthScenario.Ok(await client.PostAsync("/api/v1/setup/token", new { token = token.ToLowerInvariant().Replace('-', ' ') }));
        Assert.Equal("account", AuthScenario.Ok(await client.GetAsync("/api/v1/enrolment")).Value("next"));

        var common = await client.PostAsync("/api/v1/enrolment/account", new { userName = "pat.admin", displayName = "Pat Admin", password = "password1234" });
        Assert.Equal(HttpStatusCode.BadRequest, common.Status);
        Assert.Contains("PasswordCommon", common.Text, StringComparison.Ordinal);
        var shortPassword = await client.PostAsync("/api/v1/enrolment/account", new { userName = "pat.admin", displayName = "Pat Admin", password = "short" });
        Assert.Contains("PasswordTooShort", shortPassword.Text, StringComparison.Ordinal);
        var badName = await client.PostAsync("/api/v1/enrolment/account", new { userName = "pat admin!", displayName = "Pat Admin", password = AuthScenario.AdminPassword });
        Assert.Contains("InvalidUserName", badName.Text, StringComparison.Ordinal);

        AuthScenario.Ok(await client.PostAsync("/api/v1/enrolment/account", new { userName = "pat.admin", displayName = "Pat Admin", password = AuthScenario.AdminPassword }));
        // The account is not written before two-factor is proven (US-02).
        Assert.Equal(0, await sql.AdminScalarAsync<int>(scenario.Installation.Database, "SELECT COUNT(*) FROM cmdb.UserAccount;"));

        var key = AuthScenario.Ok(await client.PostAsync("/api/v1/enrolment/authenticator"));
        var authenticator = new Authenticator(key.Value("key"));
        var qr = await client.GetAsync("/api/v1/enrolment/authenticator.svg");
        Assert.Equal(HttpStatusCode.OK, qr.Status);
        Assert.Equal("image/svg+xml", qr.ContentType);
        Assert.Equal("no-store", qr.CacheControl);
        Assert.StartsWith("<svg", qr.Text, StringComparison.Ordinal);

        Assert.Equal("invalid-code", (await client.PostAsync("/api/v1/enrolment/complete", new { code = "000000" })).Code);
        var done = AuthScenario.Ok(await client.PostAsync("/api/v1/enrolment/complete", new { code = await authenticator.NextCodeAsync() }));

        var codes = AuthScenario.Strings(done.Body.GetProperty("recoveryCodes"));
        Assert.Equal(10, codes.Count);
        Assert.All(codes, c => Assert.Matches("^[23456789BCDFGHJKMNPQRTVWXY]{5}-[23456789BCDFGHJKMNPQRTVWXY]{5}$", c));
        Assert.Equal(["Admin"], AuthScenario.Strings(done.Body.GetProperty("user").GetProperty("roles")));
        var session = AuthScenario.Ok(await client.GetAsync("/api/v1/auth/session"));
        Assert.Equal("pat.admin", session.Body.GetProperty("user").GetProperty("userName").GetString());
        AuthScenario.Ok(await client.GetAsync("/api/v1/users"));

        // Stored hashed or encrypted only: no code, key or password in the database.
        var stored = await sql.AdminScalarAsync<string>(scenario.Installation.Database, """
            SELECT CONCAT((SELECT STRING_AGG(CodeHash, ',') FROM cmdb.RecoveryCode), '|', PasswordHash, '|', AuthenticatorKey)
            FROM cmdb.UserAccount;
            """);
        Assert.DoesNotContain(codes[0], stored, StringComparison.Ordinal);
        Assert.DoesNotContain(authenticator.Key, stored, StringComparison.Ordinal);
        Assert.DoesNotContain(AuthScenario.AdminPassword, stored, StringComparison.Ordinal);
        Assert.Equal(1, await sql.AdminScalarAsync<int>(scenario.Installation.Database, "SELECT COUNT(*) FROM cmdb.Installation;"));
        Assert.Single(await scenario.AuditAsync("setup.completed"));

        // Setup is disabled permanently: now, and after a restart (no new token).
        Assert.Equal("setup-complete", (await scenario.App.Client().PostAsync("/api/v1/setup/token", new { token })).Code);
        await scenario.RestartAsync();
        var restarted = scenario.App.Client();
        await scenario.App.Logs.WaitForAsync(_ => false, TimeSpan.FromSeconds(3));
        Assert.Equal("complete", AuthScenario.Ok(await restarted.GetAsync("/api/v1/setup")).Value("status"));
        Assert.DoesNotContain(scenario.App.Logs.Events, GoldmarkApp.IsSetupToken);
    }

    [Fact]
    public async Task The_setup_token_is_logged_exactly_once_and_no_other_secret_is_logged()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, person) = await scenario.SetUpAsync();
        var (token, code, _) = await AuthScenario.InviteAsync(admin, "sam.rivera", "Viewer");
        var (_, sam) = await scenario.AcceptAsync(token, code, "sam.rivera", "sam rivera likes long walks 93");
        var signedIn = await scenario.SignInAsync(sam);

        var logs = scenario.App.Logs.RenderAll();
        var setupToken = await scenario.App.SetupTokenAsync();
        Assert.Single(scenario.App.Logs.Events, GoldmarkApp.IsSetupToken);
        Assert.Contains("Setup token", logs, StringComparison.Ordinal);
        Assert.Single(scenario.App.Logs.RenderEach(), line => line.Contains(setupToken, StringComparison.Ordinal));
        foreach (var secret in new[] { person.Password, sam.Password, token, code, person.RecoveryCodes[0], sam.Authenticator.Key, person.Authenticator.Key,
            scenario.Installation.RuntimePassword })
        {
            Assert.DoesNotContain(secret, logs, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("__Host-goldmark", logs, StringComparison.Ordinal);
        var audit = await scenario.AuditTextAsync() ?? "";
        foreach (var secret in new[] { person.Password, sam.Password, token, code, person.RecoveryCodes[0], setupToken })
        {
            Assert.DoesNotContain(secret, audit, StringComparison.Ordinal);
        }

        signedIn.Dispose();
    }

    [Fact]
    public async Task Sign_in_needs_the_password_and_then_a_code_that_works_once()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (_, admin) = await scenario.SetUpAsync();
        var client = scenario.App.Client();

        var wrong = await client.PostAsync("/api/v1/auth/sign-in", new { userName = admin.UserName, password = "not the password at all" });
        var unknown = await client.PostAsync("/api/v1/auth/sign-in", new { userName = "nobody.here", password = "not the password at all" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.Status);
        Assert.Equal((wrong.Status, wrong.Code, wrong.Body.GetProperty("title").GetString()),
            (unknown.Status, unknown.Code, unknown.Body.GetProperty("title").GetString()));

        // The second step needs the first: without the pending cookie it is 401.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/v1/auth/sign-in/two-factor", new { code = "123456" })).Status);

        AuthScenario.Ok(await client.PostAsync("/api/v1/auth/sign-in", new { userName = admin.UserName.ToUpperInvariant(), password = admin.Password }));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/users")).Status); // not signed in yet
        Assert.Equal("invalid-code", (await client.PostAsync("/api/v1/auth/sign-in/two-factor", new { code = admin.Authenticator.LastCode() })).Code);
        var signedIn = AuthScenario.Ok(await client.PostAsync("/api/v1/auth/sign-in/two-factor", new { code = await admin.Authenticator.NextCodeAsync() }));
        Assert.Equal(admin.Id, signedIn.Body.GetProperty("user").GetProperty("id").GetGuid());
        AuthScenario.Ok(await client.GetAsync("/api/v1/users"));

        // The same code a second time, for another sign-in, is refused (owner, DEC-040).
        var other = scenario.App.Client();
        AuthScenario.Ok(await other.PostAsync("/api/v1/auth/sign-in", new { userName = admin.UserName, password = admin.Password }));
        Assert.Equal("invalid-code", (await other.PostAsync("/api/v1/auth/sign-in/two-factor", new { code = admin.Authenticator.LastCode() })).Code);

        // A recovery code works once.
        var recovery = scenario.App.Client();
        AuthScenario.Ok(await recovery.PostAsync("/api/v1/auth/sign-in", new { userName = admin.UserName, password = admin.Password }));
        AuthScenario.Ok(await recovery.PostAsync("/api/v1/auth/sign-in/two-factor", new { recoveryCode = admin.RecoveryCodes[0].ToLowerInvariant() }));
        var again = scenario.App.Client();
        AuthScenario.Ok(await again.PostAsync("/api/v1/auth/sign-in", new { userName = admin.UserName, password = admin.Password }));
        Assert.Equal("invalid-recovery-code", (await again.PostAsync("/api/v1/auth/sign-in/two-factor", new { recoveryCode = admin.RecoveryCodes[0] })).Code);
        Assert.Single(await scenario.AuditAsync("auth.recovery-code.used"));
        Assert.Equal(9, AuthScenario.Ok(await recovery.GetAsync("/api/v1/account/two-factor")).Body.GetProperty("recoveryCodesRemaining").GetInt32());

        var failures = await scenario.AuditAsync("auth.sign-in.failed");
        Assert.Contains(failures, f => f.Contains("unknown-user", StringComparison.Ordinal) && f.StartsWith('|'));
        Assert.Contains(failures, f => f.Contains("\"reason\":\"password\"", StringComparison.Ordinal) && f.StartsWith(admin.Id.ToString("D"), StringComparison.Ordinal));
        Assert.Contains(failures, f => f.Contains("recovery-code", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Signing_out_ends_the_session_on_the_server_so_a_copied_cookie_stops_working()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (client, admin) = await scenario.SetUpAsync();
        var copied = client.CookieHeader();
        Assert.Contains("__Host-goldmark=", copied, StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/auth/sign-out")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/users")).Status);

        using var thief = scenario.App.CreateDefaultClient(new Uri("https://localhost"));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users");
        request.Headers.Add("Cookie", copied);
        using var response = await thief.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await sql.AdminScalarAsync<int>(scenario.Installation.Database, "SELECT COUNT(*) FROM cmdb.UserSession;"));
        Assert.Single(await scenario.AuditAsync("auth.signed-out"), a => a.Contains(admin.Id.ToString("D"), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sessions_end_when_idle_and_at_their_absolute_limit()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (client, admin) = await scenario.SetUpAsync();
        AuthScenario.Ok(await client.GetAsync("/api/v1/users"));

        await sql.AsAdminAsync(scenario.Installation.Database, "UPDATE cmdb.UserSession SET ExpiresAt = DATEADD(second, -1, SYSUTCDATETIME());");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/users")).Status);

        var second = await scenario.SignInAsync(admin);
        AuthScenario.Ok(await second.GetAsync("/api/v1/users"));
        await sql.AsAdminAsync(scenario.Installation.Database, """
            UPDATE cmdb.UserSession SET AbsoluteExpiresAt = DATEADD(second, -1, SYSUTCDATETIME()), ExpiresAt = DATEADD(second, -1, SYSUTCDATETIME());
            """);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/v1/users")).Status);
    }

    [Fact]
    public async Task Five_failures_lock_an_account_and_an_unknown_name_alike()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (_, admin) = await scenario.SetUpAsync();
        var client = scenario.App.Client();

        Response? last = null;
        Response? lastUnknown = null;
        for (var i = 0; i < 5; i++)
        {
            last = await client.PostAsync("/api/v1/auth/sign-in", new { userName = admin.UserName, password = $"wrong guess {i} here" });
            lastUnknown = await client.PostAsync("/api/v1/auth/sign-in", new { userName = "ghost.user", password = $"wrong guess {i} here" });
        }

        Assert.Equal("locked", last!.Code);
        Assert.Equal(Shape(last), Shape(lastUnknown!));
        // Locked: even the right password is refused, and says so.
        Assert.Equal("locked", (await client.PostAsync("/api/v1/auth/sign-in", new { userName = admin.UserName, password = admin.Password })).Code);
        Assert.Equal(15, last.Body.GetProperty("minutes").GetInt32());
        Assert.Single(await scenario.AuditAsync("auth.account.locked-out"));

        // Wrong second factors count too.
        await sql.AsAdminAsync(scenario.Installation.Database, "UPDATE cmdb.UserAccount SET LockoutEnd = NULL, AccessFailedCount = 0;");
        AuthScenario.Ok(await client.PostAsync("/api/v1/auth/sign-in", new { userName = admin.UserName, password = admin.Password }));
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal("invalid-code", (await client.PostAsync("/api/v1/auth/sign-in/two-factor", new { code = "000000" })).Code);
        }

        Assert.Equal("locked", (await client.PostAsync("/api/v1/auth/sign-in/two-factor", new { recoveryCode = "BBBBB-CCCCC" })).Code);
    }

    [Fact]
    public async Task Sign_in_requests_are_rate_limited_per_client_address()
    {
        await using var scenario = await AuthScenario.StartAsync(sql, new Dictionary<string, string?> { ["Goldmark:Auth:RequestsPerMinute"] = "3" });
        var client = scenario.App.Client();

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/v1/auth/sign-in", new { userName = "x", password = "y" })).Status);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsync("/api/v1/auth/sign-in", new { userName = "x", password = "y" })).Status);
    }

    /// <summary>What a client sees of an answer, without the per-request trace id.</summary>
    private static string Shape(Response response)
    {
        var body = response.Body.EnumerateObject().Where(p => p.Name != "traceId").Select(p => $"{p.Name}={p.Value}");
        return $"{(int)response.Status} {string.Join(';', body)}";
    }
}

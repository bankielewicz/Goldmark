using System.Net;
using System.Text.Json;
using Goldmark.Api.IntegrationTests.Infrastructure;

namespace Goldmark.Api.IntegrationTests;

/// <summary>
/// Users &amp; roles (US-04), invitation links with a separate code, two-factor reset and the guards
/// (DEC-038 items 2, 8 and 10; DEC-040), against real SQL Server 2025. Every change writes an audit row.
/// </summary>
[Collection(SqlServerTests.Name)]
public sealed class UsersAndLinksTests(SqlServerFixture sql)
{
    private static readonly string[] ViewerOnly = ["Viewer"];

    [Fact]
    public async Task An_invitation_needs_its_separate_code_and_lets_the_person_choose_a_password_and_set_up_two_factor()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, _) = await scenario.SetUpAsync();
        var (token, code, id) = await AuthScenario.InviteAsync(admin, "sam.rivera", "Viewer", "Auditor");

        var listed = AuthScenario.Ok(await admin.GetAsync("/api/v1/users")).Body.GetProperty("users").EnumerateArray()
            .Single(u => u.GetProperty("id").GetGuid() == id);
        Assert.Equal("invitation-pending", listed.GetProperty("account").GetProperty("state").GetString());
        Assert.Equal(["Viewer", "Auditor"], AuthScenario.Strings(listed.GetProperty("roles")));

        var person = scenario.App.Client();
        var check = AuthScenario.Ok(await person.PostAsync("/api/v1/links/check", new { token }));
        Assert.Equal(("valid", "invitation", 5), (check.Value("status"), check.Value("purpose"), check.Body.GetProperty("triesLeft").GetInt32()));
        var wrong = await person.PostAsync("/api/v1/links/redeem", new { token, code = WrongCode(code) });
        Assert.Equal(("wrong-code", 4), (wrong.Code, wrong.Body.GetProperty("triesLeft").GetInt32()));
        // The token alone opens nothing: the enrolment steps need the code first.
        Assert.Equal(HttpStatusCode.Unauthorized, (await person.PostAsync("/api/v1/enrolment/password", new { password = "x" })).Status);

        var redeemed = AuthScenario.Ok(await person.PostAsync("/api/v1/links/redeem", new { token, code = code.ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal) }));
        Assert.Equal("Sam Rivera", redeemed.Value("displayName"));
        Assert.Equal("PasswordCommon", (await person.PostAsync("/api/v1/enrolment/password", new { password = "Password1234" })).Body
            .GetProperty("errors").GetProperty("password")[0].GetString());
        AuthScenario.Ok(await person.PostAsync("/api/v1/enrolment/password", new { password = "sam rivera likes long walks 93" }));
        var authenticator = new Authenticator(AuthScenario.Ok(await person.PostAsync("/api/v1/enrolment/authenticator")).Value("key"));
        var done = AuthScenario.Ok(await person.PostAsync("/api/v1/enrolment/complete", new { code = await authenticator.NextCodeAsync() }));
        Assert.Equal(10, done.Body.GetProperty("recoveryCodes").GetArrayLength());

        // Signed in as a Viewer: not an administrator.
        AuthScenario.Ok(await person.GetAsync("/api/v1/account/two-factor"));
        Assert.Equal(HttpStatusCode.Forbidden, (await person.GetAsync("/api/v1/users")).Status);
        // The link worked once.
        Assert.Equal("invalid", AuthScenario.Ok(await scenario.App.Client().PostAsync("/api/v1/links/check", new { token })).Value("status"));
        Assert.Single(await scenario.AuditAsync("user.invitation.created"));
        Assert.Single(await scenario.AuditAsync("user.invitation.redeemed"));
        Assert.Single(await scenario.AuditAsync("user.link.code-failed"));
        var signedIn = await scenario.SignInAsync(new Person("sam.rivera", "sam rivera likes long walks 93", authenticator, [], id));
        AuthScenario.Ok(await signedIn.GetAsync("/api/v1/account/two-factor"));
    }

    [Fact]
    public async Task Five_wrong_codes_revoke_a_link_and_expired_links_say_so()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, _) = await scenario.SetUpAsync();
        var (token, code, id) = await AuthScenario.InviteAsync(admin, "riley.chen", "Editor");
        var person = scenario.App.Client();

        for (var i = 4; i >= 1; i--)
        {
            Assert.Equal(i, (await person.PostAsync("/api/v1/links/redeem", new { token, code = WrongCode(code) })).Body.GetProperty("triesLeft").GetInt32());
        }

        Assert.Equal("link-invalidated", (await person.PostAsync("/api/v1/links/redeem", new { token, code = WrongCode(code) })).Code);
        Assert.Equal("link-invalid", (await person.PostAsync("/api/v1/links/redeem", new { token, code })).Code);
        Assert.Single(await scenario.AuditAsync("user.link.invalidated"));

        // A new link from "Copy new link" works; the old one stays dead. Then let it expire.
        var reissued = AuthScenario.Ok(await admin.PostAsync($"/api/v1/users/{id:D}/invitation")).Body.GetProperty("link");
        var newToken = reissued.GetProperty("token").GetString()!;
        Assert.Equal("valid", AuthScenario.Ok(await person.PostAsync("/api/v1/links/check", new { token = newToken })).Value("status"));
        await sql.AsAdminAsync(scenario.Installation.Database, "UPDATE cmdb.UserInvitation SET ExpiresAt = DATEADD(minute, -1, SYSUTCDATETIME()) WHERE RevokedAt IS NULL;");
        Assert.Equal("expired", AuthScenario.Ok(await person.PostAsync("/api/v1/links/check", new { token = newToken })).Value("status"));
        Assert.Equal("link-expired", (await person.PostAsync("/api/v1/links/redeem", new { token = newToken, code = reissued.GetProperty("code").GetString() })).Code);
        var listed = AuthScenario.Ok(await admin.GetAsync($"/api/v1/users/{id:D}"));
        Assert.Equal("invitation-expired", listed.Body.GetProperty("account").GetProperty("state").GetString());
    }

    [Fact]
    public async Task Revoking_an_unused_invitation_removes_the_account_and_frees_the_user_name()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, _) = await scenario.SetUpAsync();
        var (token, _, id) = await AuthScenario.InviteAsync(admin, "riley.chen", "Viewer");

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/users/{id:D}/invitation")).Status);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/users/{id:D}")).Status);
        Assert.Equal("invalid", AuthScenario.Ok(await scenario.App.Client().PostAsync("/api/v1/links/check", new { token })).Value("status"));
        Assert.Single(await scenario.AuditAsync("user.invitation.revoked"), a => a.StartsWith(id.ToString("D"), StringComparison.Ordinal));
        await AuthScenario.InviteAsync(admin, "riley.chen", "Viewer");
    }

    [Fact]
    public async Task Invitations_never_grant_Admin_or_Collector_and_user_names_are_unique()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, _) = await scenario.SetUpAsync();

        Assert.Contains("RoleNotAllowedForInvitation", (await admin.PostAsync("/api/v1/users", new { displayName = "Eve", userName = "eve", roles = RoleList("Admin") })).Text, StringComparison.Ordinal);
        Assert.Contains("RoleNotAllowedForInvitation", (await admin.PostAsync("/api/v1/users", new { displayName = "Bot", userName = "bot", roles = RoleList("Collector") })).Text, StringComparison.Ordinal);
        Assert.Contains("RolesRequired", (await admin.PostAsync("/api/v1/users", new { displayName = "No One", userName = "no.one", roles = RoleList() })).Text, StringComparison.Ordinal);
        Assert.Contains("DuplicateUserName", (await admin.PostAsync("/api/v1/users", new { displayName = "Pat Again", userName = "PAT.ADMIN", roles = RoleList("Viewer") })).Text, StringComparison.Ordinal);
        // Collector is refused by the database too (DEC-038 item 10).
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => sql.ExecuteAsync(scenario.Installation.RuntimeUser,
            scenario.Installation.RuntimePassword, scenario.Installation.Database,
            "INSERT INTO cmdb.UserRole (UserId, Role) SELECT TOP (1) Id, 'Collector' FROM cmdb.UserAccount;"));
    }

    [Fact]
    public async Task Changing_roles_needs_the_ETag_and_keeps_an_enabled_Admin()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, adminPerson) = await scenario.SetUpAsync();
        var (token, code, pendingId) = await AuthScenario.InviteAsync(admin, "new.person", "Viewer");
        var (_, sam) = await scenario.InviteAndAcceptAsync(admin, "sam.rivera", "Viewer");
        var path = $"/api/v1/users/{sam.Id:D}/roles";
        var etag = AuthScenario.Ok(await admin.GetAsync($"/api/v1/users/{sam.Id:D}")).ETag!;

        Assert.Equal(HttpStatusCode.PreconditionRequired, (await admin.PutAsync(path, new { roles = RoleList("Editor") })).Status);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await admin.PutAsync(path, new { roles = RoleList("Editor") }, "\"stale\"")).Status);
        Assert.Contains("RoleNotAllowed", (await admin.PutAsync(path, new { roles = RoleList("Collector") }, etag)).Text, StringComparison.Ordinal);

        var changed = AuthScenario.Ok(await admin.PutAsync(path, new { roles = RoleList("Editor", "Auditor") }, etag));
        Assert.Equal(["Editor", "Auditor"], AuthScenario.Strings(changed.Body.GetProperty("roles")));
        Assert.NotEqual(etag, changed.ETag);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await admin.PutAsync(path, new { roles = RoleList("Viewer") }, etag)).Status);
        Assert.Contains(await scenario.AuditAsync("user.roles.changed"), a => a.Contains("\"after\":[\"Editor\",\"Auditor\"]", StringComparison.Ordinal));

        // Admin only for someone who has signed in (Sam has; the pending person has not).
        var pendingEtag = AuthScenario.Ok(await admin.GetAsync($"/api/v1/users/{pendingId:D}")).ETag!;
        Assert.Equal("admin-needs-sign-in", (await admin.PutAsync($"/api/v1/users/{pendingId:D}/roles", new { roles = RoleList("Admin") }, pendingEtag)).Code);
        var samAdmin = AuthScenario.Ok(await admin.PutAsync(path, new { roles = RoleList("Admin") }, changed.ETag));

        // Nobody removes Admin from themselves; the last enabled Admin keeps it.
        var adminPath = $"/api/v1/users/{adminPerson.Id:D}/roles";
        var adminEtag = AuthScenario.Ok(await admin.GetAsync($"/api/v1/users/{adminPerson.Id:D}")).ETag!;
        Assert.Equal("own-admin", (await admin.PutAsync(adminPath, new { roles = RoleList("Viewer") }, adminEtag)).Code);
        var samClient = await scenario.SignInAsync(sam);
        AuthScenario.Ok(await samClient.GetAsync("/api/v1/users")); // the new role applies at once
        AuthScenario.Ok(await samClient.PutAsync(adminPath, new { roles = RoleList("Viewer") }, adminEtag));
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/users")).Status);
        var samEtag = AuthScenario.Ok(await samClient.GetAsync($"/api/v1/users/{sam.Id:D}")).ETag!;
        Assert.Equal("own-admin", (await samClient.PutAsync(path, new { roles = RoleList("Viewer") }, samEtag)).Code);
        Assert.Equal("self", (await samClient.PostAsync($"/api/v1/users/{sam.Id:D}/disable")).Code);
        _ = (token, code, samAdmin);
    }

    [Fact]
    public async Task Locking_disabling_and_resetting_two_factor_end_the_persons_sessions()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, adminPerson) = await scenario.SetUpAsync();
        var (samClient, sam) = await scenario.InviteAndAcceptAsync(admin, "sam.rivera", "Editor");
        var user = $"/api/v1/users/{sam.Id:D}";
        AuthScenario.Ok(await samClient.GetAsync("/api/v1/account/two-factor"));

        Assert.Equal("self", (await admin.PostAsync($"/api/v1/users/{adminPerson.Id:D}/lock")).Code);
        Assert.Equal("locked", AuthScenario.Ok(await admin.PostAsync($"{user}/lock")).Body.GetProperty("account").GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await samClient.GetAsync("/api/v1/account/two-factor")).Status);
        // Locked by an administrator: only the right password learns it; a wrong one looks like any failure.
        var client = scenario.App.Client();
        Assert.Equal("invalid-credentials", (await client.PostAsync("/api/v1/auth/sign-in", new { userName = sam.UserName, password = "a wrong password!" })).Code);
        Assert.Equal("locked-by-administrator", (await client.PostAsync("/api/v1/auth/sign-in", new { userName = sam.UserName, password = sam.Password })).Code);
        AuthScenario.Ok(await admin.PostAsync($"{user}/unlock"));
        samClient = await scenario.SignInAsync(sam);

        AuthScenario.Ok(await admin.PostAsync($"{user}/disable"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await samClient.GetAsync("/api/v1/account/two-factor")).Status);
        Assert.Equal("invalid-credentials", (await client.PostAsync("/api/v1/auth/sign-in", new { userName = sam.UserName, password = sam.Password })).Code);
        AuthScenario.Ok(await admin.PostAsync($"{user}/enable"));
        samClient = await scenario.SignInAsync(sam);

        // Two-factor reset: sessions end, the password is kept, sign-in points to the new link.
        var reset = AuthScenario.Ok(await admin.PostAsync($"{user}/two-factor-reset")).Body.GetProperty("link");
        Assert.Equal(HttpStatusCode.Unauthorized, (await samClient.GetAsync("/api/v1/account/two-factor")).Status);
        Assert.Equal("two-factor-reset", (await client.PostAsync("/api/v1/auth/sign-in", new { userName = sam.UserName, password = sam.Password })).Code);
        Assert.Equal("self", (await admin.PostAsync($"/api/v1/users/{adminPerson.Id:D}/two-factor-reset")).Code);

        var token = reset.GetProperty("token").GetString()!;
        var redeemed = AuthScenario.Ok(await client.PostAsync("/api/v1/links/redeem", new { token, code = reset.GetProperty("code").GetString() }));
        Assert.Equal("two-factor-reset", redeemed.Value("purpose"));
        Assert.Contains("PasswordIncorrect", (await client.PostAsync("/api/v1/enrolment/password", new { password = "not sams password" })).Text, StringComparison.Ordinal);
        AuthScenario.Ok(await client.PostAsync("/api/v1/enrolment/password", new { password = sam.Password }));
        var authenticator = new Authenticator(AuthScenario.Ok(await client.PostAsync("/api/v1/enrolment/authenticator")).Value("key"));
        AuthScenario.Ok(await client.PostAsync("/api/v1/enrolment/complete", new { code = await authenticator.NextCodeAsync() }));
        AuthScenario.Ok(await client.GetAsync("/api/v1/account/two-factor"));
        // The old authenticator no longer signs in.
        var old = scenario.App.Client();
        AuthScenario.Ok(await old.PostAsync("/api/v1/auth/sign-in", new { userName = sam.UserName, password = sam.Password }));
        Assert.Equal("invalid-code", (await old.PostAsync("/api/v1/auth/sign-in/two-factor", new { code = await sam.Authenticator.NextCodeAsync() })).Code);

        foreach (var action in RoleList("user.locked", "user.unlocked", "user.disabled", "user.enabled", "user.two-factor.reset", "user.two-factor.reset-completed"))
        {
            Assert.Single(await scenario.AuditAsync(action));
        }
    }

    [Fact]
    public async Task The_last_enabled_Admin_cannot_be_disabled_and_export_is_CSV()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, adminPerson) = await scenario.SetUpAsync();
        var (_, sam) = await scenario.InviteAndAcceptAsync(admin, "sam.rivera", "Viewer");
        var samEtag = AuthScenario.Ok(await admin.GetAsync($"/api/v1/users/{sam.Id:D}")).ETag!;
        AuthScenario.Ok(await admin.PutAsync($"/api/v1/users/{sam.Id:D}/roles", new { roles = RoleList("Admin") }, samEtag));
        var samClient = await scenario.SignInAsync(sam);

        AuthScenario.Ok(await samClient.PostAsync($"/api/v1/users/{adminPerson.Id:D}/disable"));
        // Pat is disabled; Sam is now the only enabled Admin and cannot lose Admin.
        var etag = AuthScenario.Ok(await samClient.GetAsync($"/api/v1/users/{sam.Id:D}")).ETag!;
        Assert.Equal("own-admin", (await samClient.PutAsync($"/api/v1/users/{sam.Id:D}/roles", new { roles = RoleList("Viewer") }, etag)).Code);
        AuthScenario.Ok(await samClient.PostAsync($"/api/v1/users/{adminPerson.Id:D}/enable"));

        var csv = await samClient.GetAsync("/api/v1/users/export");
        Assert.Equal(HttpStatusCode.OK, csv.Status);
        Assert.Equal("text/csv", csv.ContentType);
        Assert.StartsWith("Name,Username,Roles,Two-factor,Account,Last sign-in (UTC)", csv.Text, StringComparison.Ordinal);
        Assert.Contains("Pat Admin,pat.admin,Admin,Enrolled,active,", csv.Text, StringComparison.Ordinal);
    }

    /// <summary>A role list for a request body (an array built per call, not a constant).</summary>
    private static string[] RoleList(params string[] roles) => roles;

    private static string WrongCode(string code) => code.StartsWith('2') ? "3" + code[1..] : "2" + code[1..];

    [Fact]
    public async Task Links_carry_the_configured_expiry_and_code_attempts()
    {
        await using var scenario = await AuthScenario.StartAsync(sql, new Dictionary<string, string?>
        {
            ["Goldmark:Auth:LinkExpiryHours"] = "2",
            ["Goldmark:Auth:LinkCodeAttempts"] = "3",
        });
        var (admin, _) = await scenario.SetUpAsync();

        // Codex, PR #24: the screen states the limits the server applies, not the defaults.
        var created = await admin.PostAsync("/api/v1/users", new { displayName = "Sam Rivera", userName = "sam.rivera", roles = ViewerOnly });
        Assert.Equal(HttpStatusCode.Created, created.Status);
        AssertLimits(created.Body.GetProperty("link"));
        var id = created.Body.GetProperty("user").GetProperty("id").GetGuid();
        AssertLimits(AuthScenario.Ok(await admin.PostAsync($"/api/v1/users/{id:D}/invitation")).Body.GetProperty("link"));

        var (_, lee) = await scenario.InviteAndAcceptAsync(admin, "lee.chen", "Viewer");
        AssertLimits(AuthScenario.Ok(await admin.PostAsync($"/api/v1/users/{lee.Id:D}/two-factor-reset")).Body.GetProperty("link"));

        static void AssertLimits(JsonElement link)
        {
            Assert.Equal(2, link.GetProperty("expiryHours").GetInt32());
            Assert.Equal(3, link.GetProperty("codeAttempts").GetInt32());
            Assert.InRange(link.GetProperty("expiresAt").GetDateTimeOffset() - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(115), TimeSpan.FromMinutes(121));
        }
    }
}

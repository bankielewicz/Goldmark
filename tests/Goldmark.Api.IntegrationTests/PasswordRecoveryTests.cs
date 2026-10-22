using System.Net;
using System.Text.Json;
using Goldmark.Api.IntegrationTests.Infrastructure;
using Serilog.Events;

namespace Goldmark.Api.IntegrationTests;

/// <summary>
/// Password recovery (slice 4a; issue #25; DEC-042) against real SQL Server 2025: an administrator's
/// password reset, the password-and-two-factor reset, dual control for the links that need none of the
/// person's own factors, Change password, and the recovery setting. Every change writes an audit row.
/// </summary>
[Collection(SqlServerTests.Name)]
public sealed class PasswordRecoveryTests(SqlServerFixture sql)
{
    private const string NewPassword = "a brand new passphrase 2026";

    private static readonly string[] EditorOnly = ["Editor"];
    private static readonly string[] ViewerOnly = ["Viewer"];
    private static readonly string[] ViewerAndAdmin = ["Viewer", "Admin"];

    [Fact]
    public async Task A_password_reset_refuses_the_old_password_and_needs_the_persons_authenticator_to_finish()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, adminPerson) = await scenario.SetUpAsync();
        var (samClient, sam) = await scenario.InviteAndAcceptAsync(admin, "sam.rivera", "Editor");

        Assert.Equal("self", (await admin.PostAsync($"/api/v1/users/{adminPerson.Id:D}/password-reset")).Code);
        var reset = AuthScenario.Ok(await admin.PostAsync($"/api/v1/users/{sam.Id:D}/password-reset"));
        var link = reset.Body.GetProperty("link");
        Assert.False(link.GetProperty("awaitingApproval").GetBoolean());
        Assert.False(link.GetProperty("onlyAdmin").GetBoolean());
        var account = reset.Body.GetProperty("user").GetProperty("account");
        Assert.True(account.GetProperty("passwordResetPending").GetBoolean());
        Assert.Equal("password-reset", account.GetProperty("link").GetProperty("purpose").GetString());

        // Every session ended; the old password no longer signs in, and failing with it does not lock the account.
        Assert.Equal(HttpStatusCode.Unauthorized, (await samClient.GetAsync("/api/v1/account/two-factor")).Status);
        var client = scenario.App.Client();
        Assert.Equal("invalid-credentials", (await client.PostAsync("/api/v1/auth/sign-in", new { userName = sam.UserName, password = sam.Password })).Code);
        Assert.Equal(0, await FailedCountAsync(scenario, sam));
        Assert.Contains(await scenario.AuditAsync("auth.sign-in.failed"), row => row.Contains("password-reset-pending", StringComparison.Ordinal));

        var person = scenario.App.Client();
        var redeemed = AuthScenario.Ok(await person.PostAsync("/api/v1/links/redeem", new { token = Token(link), code = Code(link) }));
        Assert.Equal(("password-reset", "password"), (redeemed.Value("purpose"), redeemed.Value("next")));
        Assert.Equal("confirm", AuthScenario.Ok(await person.PostAsync("/api/v1/enrolment/password", new { password = NewPassword })).Value("next"));
        // The authenticator is kept: this reset can't set up a new one.
        Assert.Equal("wrong-step", (await person.PostAsync("/api/v1/enrolment/authenticator")).Code);
        Assert.Equal("wrong-step", (await person.PostAsync("/api/v1/enrolment/complete", new { code = "123456" })).Code);

        Assert.Equal("invalid-code", (await person.PostAsync("/api/v1/enrolment/confirm", new { code = "000000" })).Code);
        Assert.Equal(1, await FailedCountAsync(scenario, sam));
        AuthScenario.Ok(await person.PostAsync("/api/v1/enrolment/confirm", new { code = await sam.Authenticator.NextCodeAsync() }));
        AuthScenario.Ok(await person.GetAsync("/api/v1/account/two-factor"));

        Assert.Equal("invalid-credentials", (await scenario.App.Client().PostAsync("/api/v1/auth/sign-in", new { userName = sam.UserName, password = sam.Password })).Code);
        AuthScenario.Ok(await (await scenario.SignInAsync(sam with { Password = NewPassword })).GetAsync("/api/v1/account/two-factor"));
        Assert.Equal("invalid", AuthScenario.Ok(await client.PostAsync("/api/v1/links/check", new { token = Token(link) })).Value("status"));

        Assert.Contains("\"twoFactorToo\":false", Assert.Single(await scenario.AuditAsync("user.password.reset")), StringComparison.Ordinal);
        var completed = Assert.Single(await scenario.AuditAsync("user.password.reset-completed"));
        Assert.Contains("\"confirmedWith\":\"authenticator-code\"", completed, StringComparison.Ordinal);
        Assert.Contains("\"issuedBy\":\"Pat Admin (pat.admin)\"", completed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_password_reset_can_be_confirmed_with_a_recovery_code()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, _) = await scenario.SetUpAsync();
        var (_, sam) = await scenario.InviteAndAcceptAsync(admin, "sam.rivera", "Viewer");
        var link = AuthScenario.Ok(await admin.PostAsync($"/api/v1/users/{sam.Id:D}/password-reset")).Body.GetProperty("link");

        var person = scenario.App.Client();
        AuthScenario.Ok(await person.PostAsync("/api/v1/links/redeem", new { token = Token(link), code = Code(link) }));
        AuthScenario.Ok(await person.PostAsync("/api/v1/enrolment/password", new { password = NewPassword }));
        Assert.Equal("invalid-recovery-code", (await person.PostAsync("/api/v1/enrolment/confirm", new { recoveryCode = "AAAAA-BBBBB" })).Code);
        var done = AuthScenario.Ok(await person.PostAsync("/api/v1/enrolment/confirm", new { recoveryCode = sam.RecoveryCodes[0] }));
        Assert.Equal(9, done.Body.GetProperty("recoveryCodesRemaining").GetInt32());
        Assert.Contains("\"confirmedWith\":\"recovery-code\"", Assert.Single(await scenario.AuditAsync("user.password.reset-completed")), StringComparison.Ordinal);
        Assert.Single(await scenario.AuditAsync("auth.recovery-code.used"));
    }

    [Fact]
    public async Task With_one_Admin_the_password_and_two_factor_reset_gives_both_values_and_is_never_for_an_Admin()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, _) = await scenario.SetUpAsync();
        var (_, sam) = await scenario.InviteAndAcceptAsync(admin, "sam.rivera", "Viewer");

        var reset = AuthScenario.Ok(await admin.PostAsync($"/api/v1/users/{sam.Id:D}/password-reset", new { alsoTwoFactor = true }));
        var link = reset.Body.GetProperty("link");
        Assert.True(link.GetProperty("onlyAdmin").GetBoolean());
        Assert.False(reset.Body.GetProperty("user").GetProperty("twoFactorEnrolled").GetBoolean());
        Assert.Equal("invalid-credentials", (await scenario.App.Client().PostAsync("/api/v1/auth/sign-in", new { userName = sam.UserName, password = sam.Password })).Code);

        var person = scenario.App.Client();
        Assert.Equal("combined-reset", AuthScenario.Ok(await person.PostAsync("/api/v1/links/redeem", new { token = Token(link), code = Code(link) })).Value("purpose"));
        Assert.Equal("authenticator", AuthScenario.Ok(await person.PostAsync("/api/v1/enrolment/password", new { password = NewPassword })).Value("next"));
        var authenticator = new Authenticator(AuthScenario.Ok(await person.PostAsync("/api/v1/enrolment/authenticator")).Value("key"));
        var done = AuthScenario.Ok(await person.PostAsync("/api/v1/enrolment/complete", new { code = await authenticator.NextCodeAsync() }));
        Assert.Equal(10, done.Body.GetProperty("recoveryCodes").GetArrayLength());
        AuthScenario.Ok(await (await scenario.SignInAsync(new Person(sam.UserName, NewPassword, authenticator, [], sam.Id))).GetAsync("/api/v1/account/two-factor"));

        var resetRow = Assert.Single(await scenario.AuditAsync("user.password.reset"));
        Assert.Contains("\"twoFactorToo\":true", resetRow, StringComparison.Ordinal);
        Assert.Contains("\"approval\":\"only-admin\"", resetRow, StringComparison.Ordinal);
        Assert.Contains("\"confirmedWith\":\"new-authenticator\"", Assert.Single(await scenario.AuditAsync("user.password.reset-completed")), StringComparison.Ordinal);

        // Never for an Admin: a link and code alone must not produce an Admin session.
        var (_, lee) = await scenario.InviteAndAcceptAsync(admin, "lee.chen", "Viewer");
        await PromoteAsync(admin, lee);
        Assert.Equal("admin-target", (await admin.PostAsync($"/api/v1/users/{lee.Id:D}/password-reset", new { alsoTwoFactor = true })).Code);
        AuthScenario.Ok(await admin.PostAsync($"/api/v1/users/{lee.Id:D}/password-reset"));
    }

    [Fact]
    public async Task Under_dual_control_the_one_who_asks_gets_the_link_and_another_Admin_gets_the_code()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (pat, _) = await scenario.SetUpAsync();
        var (_, morganPerson) = await scenario.InviteAndAcceptAsync(pat, "morgan.okafor", "Viewer");
        var (samClient, sam) = await scenario.InviteAndAcceptAsync(pat, "sam.rivera", "Viewer");
        await PromoteAsync(pat, morganPerson);
        var morgan = await scenario.SignInAsync(morganPerson);

        // An invitation: Pat gets the link only, and it does nothing until approved.
        var created = await pat.PostAsync("/api/v1/users", new { displayName = "Riley Chen", userName = "riley.chen", roles = EditorOnly });
        Assert.Equal(HttpStatusCode.Created, created.Status);
        var link = created.Body.GetProperty("link");
        Assert.True(link.GetProperty("awaitingApproval").GetBoolean());
        Assert.Equal(JsonValueKind.Null, link.GetProperty("code").ValueKind);
        var rileyId = created.Body.GetProperty("user").GetProperty("id").GetGuid();
        // The list says who asked and when, for the Approve dialog.
        var asked = created.Body.GetProperty("user").GetProperty("account").GetProperty("link").GetProperty("requestedBy");
        Assert.Equal("Pat Admin", asked.GetProperty("displayName").GetString());
        Assert.True(asked.GetProperty("askedAt").GetDateTime() > DateTime.UtcNow.AddMinutes(-5));
        var person = scenario.App.Client();
        Assert.Equal("pending", AuthScenario.Ok(await person.PostAsync("/api/v1/links/check", new { token = Token(link) })).Value("status"));
        Assert.Equal("link-pending", (await person.PostAsync("/api/v1/links/redeem", new { token = Token(link), code = "K7Q-2MX" })).Code);
        Assert.Empty(await scenario.AuditAsync("user.link.code-failed"));

        Assert.Equal(0, Waiting(AuthScenario.Ok(await pat.GetAsync("/api/v1/users/approvals"))));
        Assert.Equal(1, Waiting(AuthScenario.Ok(await morgan.GetAsync("/api/v1/users/approvals"))));
        Assert.Equal("own-request", (await pat.PostAsync($"/api/v1/users/{rileyId:D}/request/approve")).Code);
        var approved = AuthScenario.Ok(await morgan.PostAsync($"/api/v1/users/{rileyId:D}/request/approve"));
        var code = approved.Body.GetProperty("code").GetProperty("code").GetString()!;
        Assert.Equal(0, Waiting(AuthScenario.Ok(await morgan.GetAsync("/api/v1/users/approvals"))));
        Assert.Equal(5, AuthScenario.Ok(await person.PostAsync("/api/v1/links/check", new { token = Token(link) })).Body.GetProperty("triesLeft").GetInt32());
        await scenario.AcceptAsync(Token(link), code, "riley.chen", "riley chen walks the dog 61");
        Assert.Contains("\"approval\":\"requested\"", (await scenario.AuditAsync("user.invitation.created"))[^1], StringComparison.Ordinal);
        var redeemedRow = (await scenario.AuditAsync("user.invitation.redeemed"))[^1];
        Assert.Contains("\"issuedBy\":\"Pat Admin (pat.admin)\"", redeemedRow, StringComparison.Ordinal);
        Assert.Contains("\"approvedBy\":\"Morgan Okafor (morgan.okafor)\"", redeemedRow, StringComparison.Ordinal);

        // A password-and-two-factor reset changes nothing until another Admin approves it.
        var request = AuthScenario.Ok(await pat.PostAsync($"/api/v1/users/{sam.Id:D}/password-reset", new { alsoTwoFactor = true })).Body.GetProperty("link");
        Assert.True(request.GetProperty("awaitingApproval").GetBoolean());
        AuthScenario.Ok(await samClient.GetAsync("/api/v1/account/two-factor"));
        Assert.Single(await scenario.AuditAsync("user.approval.requested"));
        Assert.Empty(await scenario.AuditAsync("user.password.reset"));
        Assert.Equal("combined-reset-open", (await pat.PutAsync($"/api/v1/users/{sam.Id:D}/roles", new { roles = ViewerAndAdmin },
            (await pat.GetAsync($"/api/v1/users/{sam.Id:D}")).ETag)).Code);
        Assert.Equal("request-waiting", (await pat.PostAsync($"/api/v1/users/{sam.Id:D}/two-factor-reset")).Code);
        Assert.Equal("request-waiting", (await pat.PostAsync($"/api/v1/users/{sam.Id:D}/password-reset")).Code);

        var samCode = AuthScenario.Ok(await morgan.PostAsync($"/api/v1/users/{sam.Id:D}/request/approve")).Body.GetProperty("code").GetProperty("code").GetString()!;
        Assert.Equal(HttpStatusCode.Unauthorized, (await samClient.GetAsync("/api/v1/account/two-factor")).Status);
        Assert.Equal("invalid-credentials", (await scenario.App.Client().PostAsync("/api/v1/auth/sign-in", new { userName = sam.UserName, password = sam.Password })).Code);
        var samNew = scenario.App.Client();
        Assert.Equal("combined-reset", AuthScenario.Ok(await samNew.PostAsync("/api/v1/links/redeem", new { token = Token(request), code = samCode })).Value("purpose"));
        AuthScenario.Ok(await samNew.PostAsync("/api/v1/enrolment/password", new { password = NewPassword }));
        var authenticator = new Authenticator(AuthScenario.Ok(await samNew.PostAsync("/api/v1/enrolment/authenticator")).Value("key"));
        AuthScenario.Ok(await samNew.PostAsync("/api/v1/enrolment/complete", new { code = await authenticator.NextCodeAsync() }));
        Assert.Contains("\"approvedBy\":\"Morgan Okafor (morgan.okafor)\"", Assert.Single(await scenario.AuditAsync("user.password.reset-completed")), StringComparison.Ordinal);

        // Declining an invitation removes its unused account; only the one who asked can cancel.
        var taylor = (await pat.PostAsync("/api/v1/users", new { displayName = "Taylor Ng", userName = "taylor.ng", roles = ViewerOnly }))
            .Body.GetProperty("user").GetProperty("id").GetGuid();
        Assert.Equal("own-request", (await pat.PostAsync($"/api/v1/users/{taylor:D}/request/decline")).Code);
        Assert.Equal(HttpStatusCode.NoContent, (await morgan.PostAsync($"/api/v1/users/{taylor:D}/request/decline")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await pat.GetAsync($"/api/v1/users/{taylor:D}")).Status);
        Assert.Contains("\"accountRemoved\":true", Assert.Single(await scenario.AuditAsync("user.approval.declined")), StringComparison.Ordinal);
        var jo = (await pat.PostAsync("/api/v1/users", new { displayName = "Jo Park", userName = "jo.park", roles = ViewerOnly }))
            .Body.GetProperty("user").GetProperty("id").GetGuid();
        Assert.Equal("not-own-request", (await morgan.PostAsync($"/api/v1/users/{jo:D}/request/cancel")).Code);
        Assert.Equal(HttpStatusCode.NoContent, (await pat.PostAsync($"/api/v1/users/{jo:D}/request/cancel")).Status);
        Assert.Single(await scenario.AuditAsync("user.approval.cancelled"));
    }

    [Fact]
    public async Task A_request_that_ended_unapproved_can_be_asked_again_and_no_longer_blocks_Admin()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (pat, _) = await scenario.SetUpAsync();
        var (_, morganPerson) = await scenario.InviteAndAcceptAsync(pat, "morgan.okafor", "Viewer");
        var (_, sam) = await scenario.InviteAndAcceptAsync(pat, "sam.rivera", "Viewer");
        await PromoteAsync(pat, morganPerson);
        var morgan = await scenario.SignInAsync(morganPerson);
        AuthScenario.Ok(await pat.PostAsync($"/api/v1/users/{sam.Id:D}/password-reset", new { alsoTwoFactor = true }));
        Assert.Equal("request-waiting", (await pat.PostAsync($"/api/v1/users/{sam.Id:D}/password-reset", new { alsoTwoFactor = true })).Code);

        await sql.AsAdminAsync(scenario.Installation.Database,
            $"UPDATE cmdb.UserInvitation SET ExpiresAt = DATEADD(MINUTE, -1, SYSUTCDATETIME()) WHERE UserId = '{sam.Id:D}' AND RevokedAt IS NULL;");
        Assert.Equal("request-ended", (await morgan.PostAsync($"/api/v1/users/{sam.Id:D}/request/approve")).Code);
        Assert.Equal(0, Waiting(AuthScenario.Ok(await morgan.GetAsync("/api/v1/users/approvals"))));
        var account = AuthScenario.Ok(await pat.GetAsync($"/api/v1/users/{sam.Id:D}")).Body.GetProperty("account");
        Assert.True(account.GetProperty("link").GetProperty("awaitingApproval").GetBoolean());
        Assert.True(account.GetProperty("link").GetProperty("expired").GetBoolean());

        AuthScenario.Ok(await pat.PutAsync($"/api/v1/users/{sam.Id:D}/roles", new { roles = ViewerAndAdmin }, (await pat.GetAsync($"/api/v1/users/{sam.Id:D}")).ETag));
        AuthScenario.Ok(await pat.PutAsync($"/api/v1/users/{sam.Id:D}/roles", new { roles = ViewerOnly }, (await pat.GetAsync($"/api/v1/users/{sam.Id:D}")).ETag));
        Assert.True(AuthScenario.Ok(await pat.PostAsync($"/api/v1/users/{sam.Id:D}/password-reset", new { alsoTwoFactor = true }))
            .Body.GetProperty("link").GetProperty("awaitingApproval").GetBoolean());
        Assert.Equal(1, Waiting(AuthScenario.Ok(await morgan.GetAsync("/api/v1/users/approvals"))));

        // An invitation's request that ended is asked again the same way ("Ask again…" sends a new link).
        var created = await pat.PostAsync("/api/v1/users", new { displayName = "Riley Chen", userName = "riley.chen", roles = EditorOnly });
        var rileyId = created.Body.GetProperty("user").GetProperty("id").GetGuid();
        await sql.AsAdminAsync(scenario.Installation.Database,
            $"UPDATE cmdb.UserInvitation SET ExpiresAt = DATEADD(MINUTE, -1, SYSUTCDATETIME()) WHERE UserId = '{rileyId:D}' AND RevokedAt IS NULL;");
        Assert.Equal(1, Waiting(AuthScenario.Ok(await morgan.GetAsync("/api/v1/users/approvals"))));
        var again = AuthScenario.Ok(await pat.PostAsync($"/api/v1/users/{rileyId:D}/invitation")).Body.GetProperty("link");
        Assert.True(again.GetProperty("awaitingApproval").GetBoolean());
        Assert.Equal(JsonValueKind.Null, again.GetProperty("code").ValueKind);
        Assert.Equal(2, Waiting(AuthScenario.Ok(await morgan.GetAsync("/api/v1/users/approvals"))));
    }

    [Fact]
    public async Task Change_password_needs_the_current_one_and_ends_the_other_sessions()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, _) = await scenario.SetUpAsync();
        var (here, sam) = await scenario.InviteAndAcceptAsync(admin, "sam.rivera", "Viewer");
        var elsewhere = await scenario.SignInAsync(sam);

        var wrong = await here.PostAsync("/api/v1/account/password", new { currentPassword = "not the password", newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.Status);
        Assert.Contains("currentPassword", wrong.Text, StringComparison.Ordinal);
        Assert.Single(await scenario.AuditAsync("account.confirmation.failed"));
        Assert.Equal(HttpStatusCode.BadRequest, (await here.PostAsync("/api/v1/account/password", new { currentPassword = sam.Password, newPassword = "short" })).Status);

        var changed = AuthScenario.Ok(await here.PostAsync("/api/v1/account/password", new { currentPassword = sam.Password, newPassword = NewPassword }));
        Assert.Equal(1, changed.Body.GetProperty("otherSessionsEnded").GetInt32());
        AuthScenario.Ok(await here.GetAsync("/api/v1/account/two-factor"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await elsewhere.GetAsync("/api/v1/account/two-factor")).Status);
        Assert.Equal("invalid-credentials", (await scenario.App.Client().PostAsync("/api/v1/auth/sign-in", new { userName = sam.UserName, password = sam.Password })).Code);
        AuthScenario.Ok(await (await scenario.SignInAsync(sam with { Password = NewPassword })).GetAsync("/api/v1/account/two-factor"));
        Assert.Contains("\"otherSessionsEnded\":1", Assert.Single(await scenario.AuditAsync("account.password.changed")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_recovery_setting_logs_a_link_whose_code_only_the_operator_has_and_that_changes_nothing_until_used()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (_, adminPerson) = await scenario.SetUpAsync();
        var recover = Recover(adminPerson.UserName.ToUpperInvariant());

        // The link and its audit entry commit together; until they can, the service waits and retries.
        await sql.AsAdminAsync(scenario.Installation.Database, "DENY INSERT ON OBJECT::cmdb.AuditLog TO goldmark_runtime;");
        try
        {
            await scenario.RestartAsync(recover);
            _ = scenario.App.Services;
            await scenario.App.Logs.WaitForAsync(e => e.Any(GoldmarkApp.IsRecoveryLink), TimeSpan.FromSeconds(3));
            Assert.DoesNotContain(scenario.App.Logs.Events, GoldmarkApp.IsRecoveryLink);
            Assert.Equal(0, await sql.AdminScalarAsync<int>(scenario.Installation.Database,
                "SELECT COUNT(*) FROM cmdb.UserInvitation WHERE Purpose = 'CombinedReset';"));
        }
        finally
        {
            await sql.AsAdminAsync(scenario.Installation.Database, "REVOKE INSERT ON OBJECT::cmdb.AuditLog FROM goldmark_runtime;");
        }

        var first = await scenario.App.RecoveryLinkAsync();
        Assert.Equal(LogEventLevel.Warning, Assert.Single(scenario.App.Logs.Events, GoldmarkApp.IsRecoveryLink).Level);
        // It works for an hour (owner, DEC-042 item 6), whatever the link lifetime setting says.
        Assert.Equal(60, await sql.AdminScalarAsync<int>(scenario.Installation.Database,
            "SELECT DATEDIFF(minute, CreatedAt, ExpiresAt) FROM cmdb.UserInvitation WHERE Purpose = 'CombinedReset' AND RevokedAt IS NULL;"));

        // A second start with the setting still set prints a new link, and the first stops working.
        await scenario.RestartAsync(recover);
        var token = await scenario.App.RecoveryLinkAsync();
        var person = scenario.App.Client();
        Assert.Equal("invalid", AuthScenario.Ok(await person.PostAsync("/api/v1/links/check", new { token = first })).Value("status"));

        // Nothing changed on the account until the link is used: the old password and authenticator still sign in.
        var before = await scenario.SignInAsync(adminPerson);
        AuthScenario.Ok(await before.GetAsync("/api/v1/users"));

        // The log holds the link but never its code: a generated-looking code or a guess is a wrong try.
        Assert.Equal("wrong-code", (await person.PostAsync("/api/v1/links/redeem", new { token, code = "K7Q-2MX" })).Code);
        Assert.Equal("combined-reset", AuthScenario.Ok(await person.PostAsync("/api/v1/links/redeem",
            new { token, code = RecoveryCode.ToLowerInvariant().Replace(' ', '-') })).Value("purpose"));
        AuthScenario.Ok(await person.PostAsync("/api/v1/enrolment/password", new { password = NewPassword }));
        var authenticator = new Authenticator(AuthScenario.Ok(await person.PostAsync("/api/v1/enrolment/authenticator")).Value("key"));
        AuthScenario.Ok(await person.PostAsync("/api/v1/enrolment/complete", new { code = await authenticator.NextCodeAsync() }));
        AuthScenario.Ok(await person.GetAsync("/api/v1/users"));

        // Using it ended every other session and replaced the password.
        Assert.Equal(HttpStatusCode.Unauthorized, (await before.GetAsync("/api/v1/users")).Status);
        Assert.Equal("invalid-credentials", (await scenario.App.Client().PostAsync("/api/v1/auth/sign-in",
            new { userName = adminPerson.UserName, password = AuthScenario.AdminPassword })).Code);

        // Each start's link is "Recovery link issued" by System; only its use is a reset (nothing changed before).
        var issued = await scenario.AuditAsync("user.recovery-link.issued");
        Assert.Equal(2, issued.Count);
        Assert.All(issued, row => Assert.Contains("|System||", row, StringComparison.Ordinal));
        Assert.Empty(await scenario.AuditAsync("user.password.reset"));
        Assert.Contains("\"issuedBy\":\"server-setting\"", Assert.Single(await scenario.AuditAsync("user.password.reset-completed")),
            StringComparison.Ordinal);

        // The log holds the link once and the code never; the audit log holds neither.
        Assert.Single(scenario.App.Logs.RenderEach(), line => line.Contains(token, StringComparison.Ordinal));
        var logs = scenario.App.Logs.RenderAll();
        var audit = await scenario.AuditTextAsync() ?? "";
        foreach (var code in new[] { RecoveryCode, "HARBOURLANTERN2026" })
        {
            Assert.DoesNotContain(code, logs, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(code, audit, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(first, audit, StringComparison.Ordinal);
        Assert.DoesNotContain(token, audit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_start_without_the_recovery_setting_ends_an_unused_recovery_link()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (_, adminPerson) = await scenario.SetUpAsync();
        await scenario.RestartAsync(Recover(adminPerson.UserName));
        var token = await scenario.App.RecoveryLinkAsync();

        await scenario.RestartAsync();
        _ = scenario.App.Services;
        await scenario.App.Logs.WaitForAsync(_ => false, TimeSpan.FromSeconds(3));
        var person = scenario.App.Client();
        Assert.Equal("invalid", AuthScenario.Ok(await person.PostAsync("/api/v1/links/check", new { token })).Value("status"));
        Assert.Contains("\"reason\":\"restart\"", Assert.Single(await scenario.AuditAsync("user.link.invalidated")),
            StringComparison.Ordinal);
        Assert.Equal("Revoked", await sql.AdminScalarAsync<string>(scenario.Installation.Database,
            "SELECT RevokeReason FROM cmdb.UserInvitation WHERE Purpose = 'CombinedReset';"));

        // Another clean start finds nothing more to end.
        await scenario.RestartAsync();
        _ = scenario.App.Services;
        await scenario.App.Logs.WaitForAsync(_ => false, TimeSpan.FromSeconds(3));
        Assert.Single(await scenario.AuditAsync("user.link.invalidated"));
        AuthScenario.Ok(await (await scenario.SignInAsync(adminPerson)).GetAsync("/api/v1/users"));
    }

    [Fact]
    public async Task The_recovery_setting_makes_no_link_for_anyone_but_an_enabled_Admin_not_locked_by_an_administrator()
    {
        // Before setup, nobody is an Admin.
        await using var scenario = await AuthScenario.StartAsync(sql, Recover("pat.admin"));
        Assert.Equal(LogEventLevel.Warning, (await scenario.App.EventAsync(3005)).Level);

        var (admin, _) = await scenario.SetUpAsync();
        var (_, sam) = await scenario.InviteAndAcceptAsync(admin, "sam.rivera", "Viewer");
        var (_, morgan) = await scenario.InviteAndAcceptAsync(admin, "morgan.okafor", "Viewer");
        await PromoteAsync(admin, morgan);
        AuthScenario.Ok(await admin.PostAsync($"/api/v1/users/{morgan.Id:D}/lock"));

        foreach (var (userName, refusal) in new[] { (sam.UserName, 3005), ("nobody.here", 3005), (morgan.UserName, 3006) })
        {
            await scenario.RestartAsync(Recover(userName));
            Assert.Equal(LogEventLevel.Warning, (await scenario.App.EventAsync(refusal)).Level);
            Assert.DoesNotContain(scenario.App.Logs.Events, GoldmarkApp.IsRecoveryLink);
        }

        Assert.Empty(await scenario.AuditAsync("user.recovery-link.issued"));
        Assert.Equal(0, await sql.AdminScalarAsync<int>(scenario.Installation.Database,
            "SELECT COUNT(*) FROM cmdb.UserInvitation WHERE Purpose = 'CombinedReset';"));
    }

    [Fact]
    public async Task A_password_reset_and_its_audit_entry_commit_together()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, _) = await scenario.SetUpAsync();
        var (samClient, sam) = await scenario.InviteAndAcceptAsync(admin, "sam.rivera", "Viewer");

        await sql.AsAdminAsync(scenario.Installation.Database, "DENY INSERT ON OBJECT::cmdb.AuditLog TO goldmark_runtime;");
        try
        {
            Assert.NotEqual(HttpStatusCode.OK, (await admin.PostAsync($"/api/v1/users/{sam.Id:D}/password-reset")).Status);
        }
        finally
        {
            await sql.AsAdminAsync(scenario.Installation.Database, "REVOKE INSERT ON OBJECT::cmdb.AuditLog FROM goldmark_runtime;");
        }

        Assert.Equal(0, await sql.AdminScalarAsync<int>(scenario.Installation.Database,
            $"SELECT COUNT(*) FROM cmdb.UserAccount WHERE Id = '{sam.Id:D}' AND PasswordResetAt IS NOT NULL;"));
        Assert.Equal(0, await sql.AdminScalarAsync<int>(scenario.Installation.Database,
            $"SELECT COUNT(*) FROM cmdb.UserInvitation WHERE UserId = '{sam.Id:D}' AND Purpose = 'PasswordReset';"));
        AuthScenario.Ok(await samClient.GetAsync("/api/v1/account/two-factor"));
    }

    // Review of PR #38 (Codex): the regressions, each first reproduced as a failing test.

    [Fact]
    public async Task A_password_only_reset_needs_an_authenticator_to_confirm_it()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (pat, _) = await scenario.SetUpAsync();
        var (_, sam) = await scenario.InviteAndAcceptAsync(pat, "sam.rivera", "Viewer");
        var (_, morgan) = await scenario.InviteAndAcceptAsync(pat, "morgan.okafor", "Viewer");
        await PromoteAsync(pat, morgan);
        AuthScenario.Ok(await pat.PostAsync($"/api/v1/users/{sam.Id:D}/two-factor-reset"));
        AuthScenario.Ok(await pat.PostAsync($"/api/v1/users/{morgan.Id:D}/two-factor-reset"));

        // Its link would end with their own authenticator, which is gone: refused, and nothing changed.
        var refused = await pat.PostAsync($"/api/v1/users/{sam.Id:D}/password-reset");
        Assert.Equal("no-authenticator", refused.Code);
        Assert.Contains("Also reset two-factor", refused.Text, StringComparison.Ordinal);
        Assert.Equal("no-authenticator", (await pat.PostAsync($"/api/v1/users/{morgan.Id:D}/password-reset")).Code);
        Assert.Equal("admin-target", (await pat.PostAsync($"/api/v1/users/{morgan.Id:D}/password-reset", new { alsoTwoFactor = true })).Code);
        Assert.Equal(0, await sql.AdminScalarAsync<int>(scenario.Installation.Database,
            "SELECT COUNT(*) FROM cmdb.UserAccount WHERE PasswordResetAt IS NOT NULL;"));
        Assert.Empty(await scenario.AuditAsync("user.password.reset"));

        // The password-and-two-factor reset is the way for Sam, but it needs another Admin's approval, and
        // Morgan, the only other Admin, has no authenticator either: refused, saying so.
        Assert.Contains("Morgan Okafor has no authenticator yet",
            (await pat.PostAsync($"/api/v1/users/{sam.Id:D}/password-reset", new { alsoTwoFactor = true })).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_request_is_refused_with_reasons_when_no_other_Admin_can_sign_in_to_approve()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (pat, _) = await scenario.SetUpAsync();
        var (_, morgan) = await scenario.InviteAndAcceptAsync(pat, "morgan.okafor", "Viewer");
        var (_, taylor) = await scenario.InviteAndAcceptAsync(pat, "taylor.ng", "Viewer");
        var (_, jo) = await scenario.InviteAndAcceptAsync(pat, "jo.park", "Viewer");
        var (_, sam) = await scenario.InviteAndAcceptAsync(pat, "sam.rivera", "Viewer");
        foreach (var person in new[] { morgan, taylor, jo })
        {
            await PromoteAsync(pat, person);
        }

        var waiting = await pat.PostAsync("/api/v1/users", new { displayName = "Riley One", userName = "riley.one", roles = ViewerOnly });
        Assert.True(waiting.Body.GetProperty("link").GetProperty("awaitingApproval").GetBoolean());
        // A waiting invitation is approved, declined or cancelled first, as for the resets.
        var rileyOne = waiting.Body.GetProperty("user").GetProperty("id").GetGuid();
        Assert.Equal("request-waiting", (await pat.PostAsync($"/api/v1/users/{rileyOne:D}/invitation")).Code);

        AuthScenario.Ok(await pat.PostAsync($"/api/v1/users/{morgan.Id:D}/password-reset"));
        AuthScenario.Ok(await pat.PostAsync($"/api/v1/users/{taylor.Id:D}/lock"));
        AuthScenario.Ok(await pat.PostAsync($"/api/v1/users/{jo.Id:D}/two-factor-reset"));

        // No request waits for an approval that can't come (owner: "C. Refuse, say why"); nothing is created.
        var refused = await pat.PostAsync("/api/v1/users", new { displayName = "Riley Two", userName = "riley.two", roles = ViewerOnly });
        Assert.Equal("no-approver-available", refused.Code);
        Assert.Contains("Taylor Ng is locked by an administrator", refused.Text, StringComparison.Ordinal);
        Assert.Contains("Morgan Okafor's password reset isn't finished", refused.Text, StringComparison.Ordinal);
        Assert.Contains("Jo Park has no authenticator yet", refused.Text, StringComparison.Ordinal);
        Assert.Equal(0, await sql.AdminScalarAsync<int>(scenario.Installation.Database,
            "SELECT COUNT(*) FROM cmdb.UserAccount WHERE NormalizedUserName = 'RILEY.TWO';"));
        Assert.Equal("no-approver-available", (await pat.PostAsync($"/api/v1/users/{sam.Id:D}/password-reset", new { alsoTwoFactor = true })).Code);
        // Only Morgan's reset above; the refused request wrote nothing.
        Assert.Single(await scenario.AuditAsync("user.password.reset"));
        Assert.Single(await scenario.AuditAsync("user.invitation.created"), row => row.Contains("riley.one", StringComparison.Ordinal));
        Assert.Empty(await scenario.AuditAsync("user.approval.requested"));

        // An automatic lockout never counts: failed sign-ins in an Admin's name can't change who approves.
        AuthScenario.Ok(await pat.PostAsync($"/api/v1/users/{taylor.Id:D}/unlock"));
        for (var i = 0; i < 5; i++)
        {
            await scenario.App.Client().PostAsync("/api/v1/auth/sign-in", new { userName = taylor.UserName, password = "not the password at all" });
        }

        Assert.Contains("locked", (await scenario.App.Client().PostAsync("/api/v1/auth/sign-in",
            new { userName = taylor.UserName, password = taylor.Password })).Code, StringComparison.Ordinal);
        var again = await pat.PostAsync("/api/v1/users", new { displayName = "Riley Three", userName = "riley.three", roles = ViewerOnly });
        Assert.True(again.Body.GetProperty("link").GetProperty("awaitingApproval").GetBoolean());
    }

    [Fact]
    public async Task Approving_records_the_approval_before_it_changes_the_account()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (pat, _) = await scenario.SetUpAsync();
        var (_, morganPerson) = await scenario.InviteAndAcceptAsync(pat, "morgan.okafor", "Viewer");
        var (_, sam) = await scenario.InviteAndAcceptAsync(pat, "sam.rivera", "Viewer");
        await PromoteAsync(pat, morganPerson);
        var morgan = await scenario.SignInAsync(morganPerson);
        AuthScenario.Ok(await pat.PostAsync($"/api/v1/users/{sam.Id:D}/password-reset", new { alsoTwoFactor = true }));

        // Fails the transaction if the reset reaches the account while the request is still unapproved: with
        // the approval first, a request that ended meanwhile leaves the account as it was (Codex, PR #38).
        await sql.AsAdminAsync(scenario.Installation.Database, """
            CREATE TRIGGER cmdb.trg_test_account_after_approval ON cmdb.UserAccount AFTER UPDATE AS
            SET NOCOUNT ON;
            IF EXISTS (SELECT 1 FROM inserted AS a JOIN cmdb.UserInvitation AS l ON l.UserId = a.Id
                       WHERE a.PasswordResetAt IS NOT NULL AND l.ApprovalRequired = 1 AND l.ApprovedAt IS NULL AND l.RevokedAt IS NULL)
                THROW 50001, 'The account changed before the approval was recorded.', 1;
            """);
        var approved = AuthScenario.Ok(await morgan.PostAsync($"/api/v1/users/{sam.Id:D}/request/approve"));
        Assert.False(string.IsNullOrEmpty(approved.Body.GetProperty("code").GetProperty("code").GetString()));
        Assert.Single(await scenario.AuditAsync("user.approval.approved"));
        Assert.Equal(1, await sql.AdminScalarAsync<int>(scenario.Installation.Database,
            $"SELECT COUNT(*) FROM cmdb.UserAccount WHERE Id = '{sam.Id:D}' AND PasswordResetAt IS NOT NULL AND AuthenticatorKey IS NULL;"));
    }

    [Fact]
    public async Task Each_start_ends_every_unused_recovery_link_whoever_it_was_for()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (pat, patPerson) = await scenario.SetUpAsync();
        var (_, morgan) = await scenario.InviteAndAcceptAsync(pat, "morgan.okafor", "Viewer");
        await PromoteAsync(pat, morgan);

        await scenario.RestartAsync(Recover(patPerson.UserName));
        var first = await scenario.App.RecoveryLinkAsync();
        await scenario.RestartAsync(Recover(morgan.UserName));
        var second = await scenario.App.RecoveryLinkAsync();
        var client = scenario.App.Client();
        Assert.Equal("invalid", AuthScenario.Ok(await client.PostAsync("/api/v1/links/check", new { token = first })).Value("status"));
        Assert.Equal("valid", AuthScenario.Ok(await client.PostAsync("/api/v1/links/check", new { token = second })).Value("status"));

        // A start whose setting it refuses ends the previous link too.
        await scenario.RestartAsync(Recover("nobody.here"));
        _ = await scenario.App.EventAsync(3005);
        Assert.Equal("invalid", AuthScenario.Ok(await scenario.App.Client().PostAsync("/api/v1/links/check", new { token = second })).Value("status"));
        Assert.Equal(0, await sql.AdminScalarAsync<int>(scenario.Installation.Database,
            "SELECT COUNT(*) FROM cmdb.UserInvitation WHERE CreatedBy IS NULL AND RedeemedAt IS NULL AND RevokedAt IS NULL;"));
        Assert.Equal(2, (await scenario.AuditAsync("user.link.invalidated")).Count);
    }

    [Fact]
    public async Task Admin_is_not_added_while_a_password_reset_is_unfinished()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (pat, _) = await scenario.SetUpAsync();
        var (_, morganPerson) = await scenario.InviteAndAcceptAsync(pat, "morgan.okafor", "Viewer");
        var (_, sam) = await scenario.InviteAndAcceptAsync(pat, "sam.rivera", "Viewer");
        await PromoteAsync(pat, morganPerson);
        var morgan = await scenario.SignInAsync(morganPerson);
        AuthScenario.Ok(await pat.PostAsync($"/api/v1/users/{sam.Id:D}/password-reset", new { alsoTwoFactor = true }));
        AuthScenario.Ok(await morgan.PostAsync($"/api/v1/users/{sam.Id:D}/request/approve"));
        await sql.AsAdminAsync(scenario.Installation.Database,
            $"UPDATE cmdb.UserInvitation SET ExpiresAt = DATEADD(MINUTE, -1, SYSUTCDATETIME()) WHERE UserId = '{sam.Id:D}' AND RedeemedAt IS NULL AND RevokedAt IS NULL;");

        // The link lapsed unused: Sam still has no password and no authenticator, so Admin must wait.
        var grant = await pat.PutAsync($"/api/v1/users/{sam.Id:D}/roles", new { roles = ViewerAndAdmin }, (await pat.GetAsync($"/api/v1/users/{sam.Id:D}")).ETag);
        Assert.Equal("password-reset-pending", grant.Code);
        Assert.True(AuthScenario.Ok(await pat.PostAsync($"/api/v1/users/{sam.Id:D}/password-reset", new { alsoTwoFactor = true }))
            .Body.GetProperty("link").GetProperty("awaitingApproval").GetBoolean());
    }

    private static async Task PromoteAsync(ApiClient admin, Person person)
    {
        var etag = (await admin.GetAsync($"/api/v1/users/{person.Id:D}")).ETag;
        AuthScenario.Ok(await admin.PutAsync($"/api/v1/users/{person.Id:D}/roles", new { roles = ViewerAndAdmin }, etag));
    }

    private static Task<int> FailedCountAsync(AuthScenario scenario, Person person) =>
        scenario.Sql.AdminScalarAsync<int>(scenario.Installation.Database,
            $"SELECT AccessFailedCount FROM cmdb.UserAccount WHERE Id = '{person.Id:D}';");

    private static int Waiting(Response response) => response.Body.GetProperty("waiting").GetInt32();

    private static string Token(JsonElement link) => link.GetProperty("token").GetString()!;

    private static string Code(JsonElement link) => link.GetProperty("code").GetString()!;

    /// <summary>The operator's recovery code in these tests (GOLDMARK_RECOVER_CODE); matched ignoring case, spaces and hyphens.</summary>
    private const string RecoveryCode = "HARBOUR LANTERN 2026";

    private static Dictionary<string, string?> Recover(string userName) => new()
    {
        ["Goldmark:Auth:RecoverAdmin"] = userName,
        ["Goldmark:Auth:RecoverAdminCode"] = RecoveryCode,
    };
}

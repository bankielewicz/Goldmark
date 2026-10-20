using System.Net;
using Goldmark.Api.IntegrationTests.Infrastructure;
using Goldmark.Api.Security;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Goldmark.Api.IntegrationTests;

/// <summary>
/// The self-service two-factor page (DEC-038 item 9) and the M6 enumeration with real sessions: a
/// signed-in person without the role gets 403 from every Admin endpoint, a password-only sign-in
/// reaches nothing but the second step, and no state-changing request works without the
/// anti-forgery token.
/// </summary>
[Collection(SqlServerTests.Name)]
public sealed class AccountAndAccessTests(SqlServerFixture sql)
{
    [Fact]
    public async Task New_recovery_codes_need_the_password_and_a_code_and_replace_the_old_ones()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (client, admin) = await scenario.SetUpAsync();

        Assert.Equal(10, AuthScenario.Ok(await client.GetAsync("/api/v1/account/two-factor")).Body.GetProperty("recoveryCodesRemaining").GetInt32());
        Assert.Contains("PasswordIncorrect", (await client.PostAsync("/api/v1/account/two-factor/recovery-codes",
            new { password = "not it at all", code = await admin.Authenticator.NextCodeAsync() })).Text, StringComparison.Ordinal);
        Assert.Contains("CodeIncorrect", (await client.PostAsync("/api/v1/account/two-factor/recovery-codes",
            new { password = admin.Password, code = "000000" })).Text, StringComparison.Ordinal);
        Assert.Equal(2, (await scenario.AuditAsync("account.confirmation.failed")).Count);

        var fresh = AuthScenario.Strings(AuthScenario.Ok(await client.PostAsync("/api/v1/account/two-factor/recovery-codes",
            new { password = admin.Password, code = await admin.Authenticator.NextCodeAsync() })).Body.GetProperty("recoveryCodes"));
        Assert.Equal(10, fresh.Count);
        Assert.Empty(fresh.Intersect(admin.RecoveryCodes));
        Assert.Single(await scenario.AuditAsync("account.recovery-codes.regenerated"));

        var old = scenario.App.Client();
        AuthScenario.Ok(await old.PostAsync("/api/v1/auth/sign-in", new { userName = admin.UserName, password = admin.Password }));
        Assert.Equal("invalid-recovery-code", (await old.PostAsync("/api/v1/auth/sign-in/two-factor", new { recoveryCode = admin.RecoveryCodes[1] })).Code);
        AuthScenario.Ok(await old.PostAsync("/api/v1/auth/sign-in/two-factor", new { recoveryCode = fresh[0] }));
    }

    [Fact]
    public async Task A_new_authenticator_replaces_the_old_one_and_ends_other_sessions()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (client, admin) = await scenario.SetUpAsync();
        var elsewhere = await scenario.SignInAsync(admin);

        // The old phone is gone: a recovery code confirms instead of a code.
        AuthScenario.Ok(await client.PostAsync("/api/v1/account/two-factor/authenticator", new { password = admin.Password, recoveryCode = admin.RecoveryCodes[0] }));
        Assert.Equal("authenticator", AuthScenario.Ok(await client.GetAsync("/api/v1/enrolment")).Value("next"));
        var next = new Authenticator(AuthScenario.Ok(await client.PostAsync("/api/v1/enrolment/authenticator")).Value("key"));
        Assert.Equal("invalid-code", (await client.PostAsync("/api/v1/account/two-factor/authenticator/verify", new { code = await admin.Authenticator.NextCodeAsync() })).Code);
        AuthScenario.Ok(await client.PostAsync("/api/v1/account/two-factor/authenticator/verify", new { code = await next.NextCodeAsync() }));

        AuthScenario.Ok(await client.GetAsync("/api/v1/account/two-factor")); // this session continues
        Assert.Equal(HttpStatusCode.Unauthorized, (await elsewhere.GetAsync("/api/v1/account/two-factor")).Status);
        Assert.Single(await scenario.AuditAsync("account.authenticator.changed"));
        var signIn = scenario.App.Client();
        AuthScenario.Ok(await signIn.PostAsync("/api/v1/auth/sign-in", new { userName = admin.UserName, password = admin.Password }));
        Assert.Equal("invalid-code", (await signIn.PostAsync("/api/v1/auth/sign-in/two-factor", new { code = await admin.Authenticator.NextCodeAsync() })).Code);
        AuthScenario.Ok(await signIn.PostAsync("/api/v1/auth/sign-in/two-factor", new { code = await next.NextCodeAsync() }));
    }

    [Fact]
    public async Task Each_sign_in_gets_a_new_session_and_ends_the_one_the_browser_presented()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, adminPerson) = await scenario.SetUpAsync();
        var (_, sam) = await scenario.InviteAndAcceptAsync(admin, "sam.rivera", "Viewer");
        var planted = admin.CookieHeader();

        // Someone plants the administrator's session cookie in a browser where Sam then signs in.
        var browser = scenario.App.Client();
        browser.Cookies.SetCookies(new Uri("https://localhost/"), planted.Split("; ").First(c => c.StartsWith("__Host-goldmark=", StringComparison.Ordinal)) + "; secure; path=/");
        AuthScenario.Ok(await browser.PostAsync("/api/v1/auth/sign-in", new { userName = sam.UserName, password = sam.Password }));
        var signedIn = AuthScenario.Ok(await browser.PostAsync("/api/v1/auth/sign-in/two-factor", new { code = await sam.Authenticator.NextCodeAsync() }));

        Assert.Equal(sam.Id, signedIn.Body.GetProperty("user").GetProperty("id").GetGuid());
        Assert.DoesNotContain(planted.Split("; ").First(c => c.StartsWith("__Host-goldmark=", StringComparison.Ordinal)), browser.CookieHeader(), StringComparison.Ordinal);
        // The planted session was ended, not turned into Sam's: its holder is signed out.
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/api/v1/users")).Status);
        Assert.Equal(sam.Id, AuthScenario.Ok(await browser.GetAsync("/api/v1/auth/session")).Body.GetProperty("user").GetProperty("id").GetGuid());
        _ = adminPerson;
    }

    [Fact]
    public async Task Every_endpoint_enforces_its_policy_with_real_sessions_and_anti_forgery()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, adminPerson) = await scenario.SetUpAsync();
        var (viewer, viewerPerson) = await scenario.InviteAndAcceptAsync(admin, "vic.viewer", "Viewer");
        var pending = scenario.App.Client();
        AuthScenario.Ok(await pending.PostAsync("/api/v1/auth/sign-in", new { userName = adminPerson.UserName, password = adminPerson.Password }));

        var endpoints = scenario.App.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/v1/", StringComparison.Ordinal) == true)
            .ToList();
        Assert.True(endpoints.Count >= 30, $"only {endpoints.Count} API endpoints found");
        foreach (var endpoint in endpoints)
        {
            var name = endpoint.DisplayName!;
            var policy = endpoint.Metadata.GetMetadata<EndpointAccess>()!.Policy;
            var path = endpoint.RoutePattern.RawText!.Replace("{id:guid}", viewerPerson.Id.ToString("D"), StringComparison.Ordinal);
            var method = new HttpMethod(endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods[0]);
            var body = method == HttpMethod.Get || method == HttpMethod.Delete ? null : new { };

            if (policy == AccessPolicy.Admin)
            {
                Assert.True((await viewer.SendAsync(method, path, body)).Status == HttpStatusCode.Forbidden, $"{name}: a Viewer must get 403");
            }

            if (policy is AccessPolicy.Admin or AccessPolicy.SignedIn)
            {
                // A password without the second factor is not a session.
                Assert.True((await pending.SendAsync(method, path, body)).Status == HttpStatusCode.Unauthorized, $"{name}: password-only must get 401");
            }

            if (method != HttpMethod.Get && policy is AccessPolicy.Admin or AccessPolicy.SignedIn)
            {
                var forged = await admin.SendAsync(method, path, body, antiforgery: false);
                Assert.True(forged.Status == HttpStatusCode.BadRequest && forged.Text.Contains("anti-forgery", StringComparison.Ordinal),
                    $"{name}: without the anti-forgery token it must be refused, got {forged}");
            }
        }

        // A token issued before sign-in does not work once signed in: tokens are bound to the session.
        var client = scenario.App.Client();
        var before = await client.AntiforgeryTokenAsync();
        AuthScenario.Ok(await client.PostAsync("/api/v1/auth/sign-in", new { userName = adminPerson.UserName, password = adminPerson.Password }));
        AuthScenario.Ok(await client.PostAsync("/api/v1/auth/sign-in/two-factor", new { code = await adminPerson.Authenticator.NextCodeAsync() }));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/sign-out");
        request.Headers.Add(ApiClient.AntiforgeryHeader, before);
        using var response = await client.Http.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/auth/sign-out")).Status);
    }
}

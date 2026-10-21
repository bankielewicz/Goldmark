using System.Net;
using System.Text;
using Goldmark.Api.Auth;
using Goldmark.Api.Security;
using Goldmark.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Goldmark.Api.Tests;

/// <summary>
/// The M6 authorization enumeration test (PRD US-04, M6; deny by default, NFR-SEC-2). Every endpoint
/// must declare its access policy, and the inventory below must list it exactly, as must
/// docs/security/endpoint-access.md: change all three together. Every endpoint that is not anonymous
/// is called without credentials and must answer 401; every anonymous state-changing API endpoint must
/// refuse a request without the anti-forgery token. The integration tests repeat this with real
/// sessions (403 for a signed-in person without the role, 400 without the token).
/// </summary>
public sealed class EndpointInventoryTests(GoldmarkFactory factory) : IClassFixture<GoldmarkFactory>
{
    /// <summary>Display name → methods, route pattern, policy.</summary>
    public static readonly IReadOnlyDictionary<string, (string Methods, string Pattern, string Policy)> Inventory =
        new Dictionary<string, (string, string, string)>
        {
            ["Health: live"] = ("GET,HEAD", "/health/live", AccessPolicy.Anonymous),
            ["Health: ready"] = ("GET,HEAD", "/health/ready", AccessPolicy.Anonymous),
            ["Health: not found"] = ("GET,HEAD", "/health/{**path}", AccessPolicy.Anonymous),
            ["API: not found"] = ("*", "/api/{**path}", AccessPolicy.Anonymous),
            ["SPA: shell"] = ("GET,HEAD", "{**path:nonfile}", AccessPolicy.Anonymous),

            ["Auth: anti-forgery token"] = ("GET", "/api/v1/auth/antiforgery", AccessPolicy.Anonymous),
            ["Auth: session"] = ("GET", "/api/v1/auth/session", AccessPolicy.Anonymous),
            ["Auth: sign in"] = ("POST", "/api/v1/auth/sign-in", AccessPolicy.Anonymous),
            ["Auth: sign in, second factor"] = ("POST", "/api/v1/auth/sign-in/two-factor", AccessPolicy.TwoFactorPending),
            ["Auth: sign out"] = ("POST", "/api/v1/auth/sign-out", AccessPolicy.SignedIn),

            ["Setup: status"] = ("GET", "/api/v1/setup", AccessPolicy.Anonymous),
            ["Setup: token"] = ("POST", "/api/v1/setup/token", AccessPolicy.Anonymous),
            ["Links: check"] = ("POST", "/api/v1/links/check", AccessPolicy.Anonymous),
            ["Links: confirm code"] = ("POST", "/api/v1/links/redeem", AccessPolicy.Anonymous),

            ["Enrolment: state"] = ("GET", "/api/v1/enrolment", AccessPolicy.Enrolment),
            ["Enrolment: administrator account"] = ("POST", "/api/v1/enrolment/account", AccessPolicy.Enrolment),
            ["Enrolment: password"] = ("POST", "/api/v1/enrolment/password", AccessPolicy.Enrolment),
            ["Enrolment: new authenticator"] = ("POST", "/api/v1/enrolment/authenticator", AccessPolicy.Enrolment),
            ["Enrolment: authenticator QR code"] = ("GET", "/api/v1/enrolment/authenticator.svg", AccessPolicy.Enrolment),
            ["Enrolment: verify and finish"] = ("POST", "/api/v1/enrolment/complete", AccessPolicy.Enrolment),
            ["Enrolment: confirm a password reset"] = ("POST", "/api/v1/enrolment/confirm", AccessPolicy.Enrolment),

            ["Account: two-factor status"] = ("GET", "/api/v1/account/two-factor", AccessPolicy.SignedIn),
            ["Account: new recovery codes"] = ("POST", "/api/v1/account/two-factor/recovery-codes", AccessPolicy.SignedIn),
            ["Account: start a new authenticator"] = ("POST", "/api/v1/account/two-factor/authenticator", AccessPolicy.SignedIn),
            ["Account: verify the new authenticator"] = ("POST", "/api/v1/account/two-factor/authenticator/verify", AccessPolicy.SignedIn),

            ["Users: list"] = ("GET", "/api/v1/users", AccessPolicy.Admin),
            ["Users: export CSV"] = ("GET", "/api/v1/users/export", AccessPolicy.Admin),
            ["Users: get"] = ("GET", "/api/v1/users/{id:guid}", AccessPolicy.Admin),
            ["Users: invite"] = ("POST", "/api/v1/users", AccessPolicy.Admin),
            ["Users: set roles"] = ("PUT", "/api/v1/users/{id:guid}/roles", AccessPolicy.Admin),
            ["Users: lock"] = ("POST", "/api/v1/users/{id:guid}/lock", AccessPolicy.Admin),
            ["Users: unlock"] = ("POST", "/api/v1/users/{id:guid}/unlock", AccessPolicy.Admin),
            ["Users: disable"] = ("POST", "/api/v1/users/{id:guid}/disable", AccessPolicy.Admin),
            ["Users: enable"] = ("POST", "/api/v1/users/{id:guid}/enable", AccessPolicy.Admin),
            ["Users: reset two-factor"] = ("POST", "/api/v1/users/{id:guid}/two-factor-reset", AccessPolicy.Admin),
            ["Users: new invitation link"] = ("POST", "/api/v1/users/{id:guid}/invitation", AccessPolicy.Admin),
            ["Users: revoke invitation"] = ("DELETE", "/api/v1/users/{id:guid}/invitation", AccessPolicy.Admin),

            ["Audit: list"] = ("GET", "/api/v1/audit", AccessPolicy.AuditReader),
            ["Audit: catalog"] = ("GET", "/api/v1/audit/catalog", AccessPolicy.AuditReader),
            ["Audit: people"] = ("GET", "/api/v1/audit/people", AccessPolicy.AuditReader),
            ["Audit: export CSV"] = ("POST", "/api/v1/audit/export", AccessPolicy.AuditReader),
        };

    [Fact]
    public void Every_endpoint_declares_its_access_policy_and_is_in_the_inventory()
    {
        var endpoints = Endpoints();

        var actual = endpoints.ToDictionary(e => e.DisplayName ?? "(unnamed)", e => (Methods(e), e.RoutePattern.RawText ?? "", Policy(e)));
        Assert.Equal(Inventory.OrderBy(p => p.Key), actual.OrderBy(p => p.Key));
    }

    [Fact]
    public void Anonymous_endpoints_say_why_and_others_require_exactly_their_declared_policy()
    {
        foreach (var endpoint in Endpoints())
        {
            var access = endpoint.Metadata.GetMetadata<EndpointAccess>()!;
            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).ToList();
            Assert.False(string.IsNullOrWhiteSpace(access.Rationale));
            if (access.Policy == AccessPolicy.Anonymous)
            {
                Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
                Assert.Empty(policies);
            }
            else
            {
                Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
                Assert.Equal([access.Policy], policies);
                Assert.Contains(access.Policy, AccessPolicy.Policies.Keys);
            }
        }
    }

    public static TheoryData<string> Protected =>
        [.. Inventory.Where(p => p.Value.Policy != AccessPolicy.Anonymous).Select(p => p.Key)];

    [Theory]
    [MemberData(nameof(Protected))]
    public async Task Every_protected_endpoint_answers_401_without_credentials(string name)
    {
        using var client = factory.CreateClient();
        var (methods, pattern, _) = Inventory[name];

        foreach (var method in methods.Split(','))
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), Url(pattern)) { Content = Json(method) };
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.True(response.Headers.Contains("Content-Security-Policy"), $"{name}: 401 without security headers");
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        }
    }

    public static TheoryData<string> AnonymousStateChanging =>
        [.. Inventory.Where(p => p.Value.Policy == AccessPolicy.Anonymous && p.Value.Pattern.StartsWith("/api/v1/", StringComparison.Ordinal)
            && p.Value.Methods is not ("GET" or "GET,HEAD")).Select(p => p.Key)];

    [Theory]
    [MemberData(nameof(AnonymousStateChanging))]
    public async Task Anonymous_state_changing_endpoints_refuse_a_request_without_the_antiforgery_token(string name)
    {
        using var client = factory.CreateClient();
        var (methods, pattern, _) = Inventory[name];

        using var response = await client.PostAsync(new Uri(Url(pattern), UriKind.Relative), Json(methods), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("anti-forgery", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_fallback_policy_denies_an_endpoint_without_a_policy_to_everyone()
    {
        using var client = factory.CreateClient();
        var authorization = factory.Services.GetRequiredService<IAuthorizationService>();
        var fallback = await factory.Services.GetRequiredService<IAuthorizationPolicyProvider>().GetFallbackPolicyAsync();
        var anyEndpoint = Endpoints()[0];
        using var scope = factory.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.SetEndpoint(anyEndpoint);
        var admin = AuthSetup.SessionPrincipal(new GoldmarkUser { Id = Guid.NewGuid(), UserName = "admin" }, [Roles.Admin]);

        Assert.NotNull(fallback);
        Assert.False((await authorization.AuthorizeAsync(admin, http, fallback)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(new System.Security.Claims.ClaimsPrincipal(), http, fallback)).Succeeded);

        // A request that matched no endpoint is not a resource: it goes on to its 404.
        http.SetEndpoint(null);
        Assert.True((await authorization.AuthorizeAsync(new System.Security.Claims.ClaimsPrincipal(), http, fallback)).Succeeded);
    }

    private List<RouteEndpoint> Endpoints()
    {
        using var client = factory.CreateClient();
        return [.. factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()];
    }

    private static string Methods(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<HttpMethodMetadata>() is { } metadata ? string.Join(',', metadata.HttpMethods) : "*";

    private static string Policy(RouteEndpoint endpoint) => endpoint.Metadata.GetMetadata<EndpointAccess>()?.Policy ?? "(none)";

    private static string Url(string pattern) =>
        pattern.Replace("{id:guid}", Guid.NewGuid().ToString("D"), StringComparison.Ordinal);

    private static StringContent? Json(string method) =>
        method is "GET" or "HEAD" or "DELETE" ? null : new StringContent("{}", Encoding.UTF8, "application/json");
}

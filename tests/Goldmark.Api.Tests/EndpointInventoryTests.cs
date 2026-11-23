using System.Net;
using System.Security.Claims;
using System.Text;
using Goldmark.Api.Auth;
using Goldmark.Api.Security;
using Goldmark.Api.Tests.Infrastructure;
using Goldmark.Api.Tokens;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Goldmark.Api.Tests;

/// <summary>
/// The M6 authorization enumeration test (PRD US-04, M6; deny by default, NFR-SEC-2). Every endpoint
/// must declare its access policy, and the inventory below must list it exactly, with the one API token
/// scope that reaches it if any (DEC-048 item 4), as must docs/security/endpoint-access.md: change all
/// three together. Every endpoint that is not anonymous is called without credentials and with a
/// malformed bearer token and must answer 401; every anonymous state-changing API endpoint must refuse a
/// request without the anti-forgery token. A token is judged by its scopes alone, a session by its roles
/// as before. The integration tests repeat this with real sessions and tokens (403 for a signed-in person
/// without the role and for a token without the scope, 400 without the anti-forgery token).
/// </summary>
public sealed class EndpointInventoryTests(GoldmarkFactory factory) : IClassFixture<GoldmarkFactory>
{
    /// <summary>Display name → methods, route pattern, policy, the token scope that reaches it (null: none does).</summary>
    public static readonly IReadOnlyDictionary<string, (string Methods, string Pattern, string Policy, string? Scope)> Inventory =
        new Dictionary<string, (string, string, string, string?)>
        {
            ["Health: live"] = ("GET,HEAD", "/health/live", AccessPolicy.Anonymous, null),
            ["Health: ready"] = ("GET,HEAD", "/health/ready", AccessPolicy.Anonymous, null),
            ["Health: not found"] = ("GET,HEAD", "/health/{**path}", AccessPolicy.Anonymous, null),
            ["API: not found"] = ("*", "/api/{**path}", AccessPolicy.Anonymous, null),
            ["SPA: shell"] = ("GET,HEAD", "{**path:nonfile}", AccessPolicy.Anonymous, null),

            ["Auth: anti-forgery token"] = ("GET", "/api/v1/auth/antiforgery", AccessPolicy.Anonymous, null),
            ["Auth: session"] = ("GET", "/api/v1/auth/session", AccessPolicy.Anonymous, null),
            ["Auth: sign in"] = ("POST", "/api/v1/auth/sign-in", AccessPolicy.Anonymous, null),
            ["Auth: sign in, second factor"] = ("POST", "/api/v1/auth/sign-in/two-factor", AccessPolicy.TwoFactorPending, null),
            ["Auth: sign out"] = ("POST", "/api/v1/auth/sign-out", AccessPolicy.SignedIn, null),

            ["Setup: status"] = ("GET", "/api/v1/setup", AccessPolicy.Anonymous, null),
            ["Setup: token"] = ("POST", "/api/v1/setup/token", AccessPolicy.Anonymous, null),
            ["Links: check"] = ("POST", "/api/v1/links/check", AccessPolicy.Anonymous, null),
            ["Links: confirm code"] = ("POST", "/api/v1/links/redeem", AccessPolicy.Anonymous, null),

            ["Enrolment: state"] = ("GET", "/api/v1/enrolment", AccessPolicy.Enrolment, null),
            ["Enrolment: administrator account"] = ("POST", "/api/v1/enrolment/account", AccessPolicy.Enrolment, null),
            ["Enrolment: password"] = ("POST", "/api/v1/enrolment/password", AccessPolicy.Enrolment, null),
            ["Enrolment: new authenticator"] = ("POST", "/api/v1/enrolment/authenticator", AccessPolicy.Enrolment, null),
            ["Enrolment: authenticator QR code"] = ("GET", "/api/v1/enrolment/authenticator.svg", AccessPolicy.Enrolment, null),
            ["Enrolment: verify and finish"] = ("POST", "/api/v1/enrolment/complete", AccessPolicy.Enrolment, null),
            ["Enrolment: confirm a password reset"] = ("POST", "/api/v1/enrolment/confirm", AccessPolicy.Enrolment, null),

            ["Account: two-factor status"] = ("GET", "/api/v1/account/two-factor", AccessPolicy.SignedIn, null),
            ["Account: new recovery codes"] = ("POST", "/api/v1/account/two-factor/recovery-codes", AccessPolicy.SignedIn, null),
            ["Account: start a new authenticator"] = ("POST", "/api/v1/account/two-factor/authenticator", AccessPolicy.SignedIn, null),
            ["Account: verify the new authenticator"] = ("POST", "/api/v1/account/two-factor/authenticator/verify", AccessPolicy.SignedIn, null),
            ["Account: change password"] = ("POST", "/api/v1/account/password", AccessPolicy.SignedIn, null),

            ["Users: list"] = ("GET", "/api/v1/users", AccessPolicy.Admin, null),
            ["Users: export CSV"] = ("GET", "/api/v1/users/export", AccessPolicy.Admin, null),
            ["Users: get"] = ("GET", "/api/v1/users/{id:guid}", AccessPolicy.Admin, null),
            ["Users: invite"] = ("POST", "/api/v1/users", AccessPolicy.Admin, null),
            ["Users: set roles"] = ("PUT", "/api/v1/users/{id:guid}/roles", AccessPolicy.Admin, null),
            ["Users: lock"] = ("POST", "/api/v1/users/{id:guid}/lock", AccessPolicy.Admin, null),
            ["Users: unlock"] = ("POST", "/api/v1/users/{id:guid}/unlock", AccessPolicy.Admin, null),
            ["Users: disable"] = ("POST", "/api/v1/users/{id:guid}/disable", AccessPolicy.Admin, null),
            ["Users: enable"] = ("POST", "/api/v1/users/{id:guid}/enable", AccessPolicy.Admin, null),
            ["Users: reset two-factor"] = ("POST", "/api/v1/users/{id:guid}/two-factor-reset", AccessPolicy.Admin, null),
            ["Users: new invitation link"] = ("POST", "/api/v1/users/{id:guid}/invitation", AccessPolicy.Admin, null),
            ["Users: revoke invitation"] = ("DELETE", "/api/v1/users/{id:guid}/invitation", AccessPolicy.Admin, null),
            ["Users: reset password"] = ("POST", "/api/v1/users/{id:guid}/password-reset", AccessPolicy.Admin, null),
            ["Users: approve a request"] = ("POST", "/api/v1/users/{id:guid}/request/approve", AccessPolicy.Admin, null),
            ["Users: decline a request"] = ("POST", "/api/v1/users/{id:guid}/request/decline", AccessPolicy.Admin, null),
            ["Users: cancel a request"] = ("POST", "/api/v1/users/{id:guid}/request/cancel", AccessPolicy.Admin, null),
            ["Users: requests waiting for me"] = ("GET", "/api/v1/users/approvals", AccessPolicy.Admin, null),

            ["Audit: list"] = ("GET", "/api/v1/audit", AccessPolicy.AuditReader, null),
            ["Audit: catalog"] = ("GET", "/api/v1/audit/catalog", AccessPolicy.AuditReader, null),
            ["Audit: people"] = ("GET", "/api/v1/audit/people", AccessPolicy.AuditReader, null),
            ["Audit: export CSV"] = ("POST", "/api/v1/audit/export", AccessPolicy.AuditReader, null),

            ["CIs: search"] = ("GET", "/api/v1/cis", AccessPolicy.SignedIn, TokenScopes.CisRead),
            ["CIs: get"] = ("GET", "/api/v1/cis/{id:guid}", AccessPolicy.SignedIn, TokenScopes.CisRead),
            ["CIs: history"] = ("GET", "/api/v1/cis/{id:guid}/history", AccessPolicy.SignedIn, TokenScopes.CisRead),
            ["CIs: create"] = ("POST", "/api/v1/cis", AccessPolicy.CiEditor, TokenScopes.CisWrite),
            ["CIs: change"] = ("PUT", "/api/v1/cis/{id:guid}", AccessPolicy.CiEditor, TokenScopes.CisWrite),
            ["CIs: partial save"] = ("PATCH", "/api/v1/cis/{id:guid}", AccessPolicy.CiEditor, TokenScopes.CisWrite),
            ["CIs: retire"] = ("POST", "/api/v1/cis/{id:guid}/retire", AccessPolicy.CiEditor, TokenScopes.CisWrite),
            ["CIs: restore"] = ("POST", "/api/v1/cis/{id:guid}/restore", AccessPolicy.CiEditor, TokenScopes.CisWrite),
            ["Types: list"] = ("GET", "/api/v1/types", AccessPolicy.SignedIn, TokenScopes.CisRead),
            ["Types: usage"] = ("GET", "/api/v1/types/usage", AccessPolicy.Admin, null),
            ["Types: add"] = ("POST", "/api/v1/types", AccessPolicy.Admin, null),
            ["Types: delete"] = ("DELETE", "/api/v1/types/{type}", AccessPolicy.Admin, null),
            ["Types: hide"] = ("POST", "/api/v1/types/{type}/hide", AccessPolicy.Admin, null),
            ["Types: show"] = ("POST", "/api/v1/types/{type}/show", AccessPolicy.Admin, null),
            ["Types: add an attribute"] = ("POST", "/api/v1/types/{type}/attributes", AccessPolicy.Admin, null),
            ["Types: change an attribute"] = ("PUT", "/api/v1/types/{type}/attributes/{name}", AccessPolicy.Admin, null),
            ["Types: delete an attribute"] = ("DELETE", "/api/v1/types/{type}/attributes/{name}", AccessPolicy.Admin, null),
            ["Relationship types: add"] = ("POST", "/api/v1/relationship-types", AccessPolicy.Admin, null),
            ["Relationship types: change"] = ("PUT", "/api/v1/relationship-types/{name}", AccessPolicy.Admin, null),
            ["Relationship types: delete"] = ("DELETE", "/api/v1/relationship-types/{name}", AccessPolicy.Admin, null),

            ["Relationships: a CI's"] = ("GET", "/api/v1/cis/{id:guid}/relationships", AccessPolicy.SignedIn, TokenScopes.RelationshipsRead),
            ["Relationships: a CI's redundancy groups"] = ("GET", "/api/v1/cis/{id:guid}/redundancy-groups", AccessPolicy.SignedIn, TokenScopes.RelationshipsRead),
            ["Relationships: get"] = ("GET", "/api/v1/relationships/{id:guid}", AccessPolicy.SignedIn, TokenScopes.RelationshipsRead),
            ["Relationships: add"] = ("POST", "/api/v1/relationships", AccessPolicy.CiEditor, TokenScopes.RelationshipsWrite),
            ["Relationships: change"] = ("PUT", "/api/v1/relationships/{id:guid}", AccessPolicy.CiEditor, TokenScopes.RelationshipsWrite),
            ["Relationships: delete"] = ("DELETE", "/api/v1/relationships/{id:guid}", AccessPolicy.CiEditor, TokenScopes.RelationshipsWrite),
            ["Graph: read"] = ("GET", "/api/v1/graph", AccessPolicy.SignedIn, TokenScopes.RelationshipsRead),
            ["Graph: export CSV"] = ("POST", "/api/v1/graph/export", AccessPolicy.SignedIn, TokenScopes.RelationshipsRead),

            ["API tokens: list"] = ("GET", "/api/v1/tokens", AccessPolicy.Admin, null),
            ["API tokens: create"] = ("POST", "/api/v1/tokens", AccessPolicy.Admin, null),
            ["API tokens: revoke"] = ("POST", "/api/v1/tokens/{id:guid}/revoke", AccessPolicy.Admin, null),
            ["API tokens: usage"] = ("GET", "/api/v1/tokens/{id:guid}/usage", AccessPolicy.Admin, null),
        };

    /// <summary>Bearer tokens that are no API token: each must be 401, never a session.</summary>
    public static readonly IReadOnlyList<string> MalformedTokens =
    [
        "Bearer sxt_short",
        "Bearer " + TestSecrets.BearerToken,
        "Bearer " + TestSecrets.ApiToken + "!",
        "bearer " + TestSecrets.ApiToken.ToLowerInvariant() + "x",
        "Bearer",
    ];

    [Fact]
    public void Every_endpoint_declares_its_access_policy_and_is_in_the_inventory()
    {
        var endpoints = Endpoints();

        var actual = endpoints.ToDictionary(e => e.DisplayName ?? "(unnamed)", e => (Methods(e), e.RoutePattern.RawText ?? "", Policy(e), Scope(e)));
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
                Assert.Null(access.TokenScope);
            }
            else
            {
                Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
                Assert.Equal([access.Policy], policies);
                Assert.Contains(access.Policy, AccessPolicy.Policies.Keys);
                Assert.True(access.TokenScope is null || TokenScopes.All.Contains(access.TokenScope), $"{endpoint.DisplayName}: unknown scope");
            }
        }
    }

    [Fact]
    public void A_scope_is_declared_only_with_a_policy_a_token_can_pass()
    {
        Assert.Throws<ArgumentException>(() => new RouteGroupBuilderStub().RequireAccess(AccessPolicy.SignedIn, "ingest:write"));
        Assert.Throws<ArgumentException>(() => new RouteGroupBuilderStub().RequireAccess(AccessPolicy.TwoFactorPending, TokenScopes.CisRead));
        Assert.Throws<ArgumentException>(() => new RouteGroupBuilderStub().RequireAccess(AccessPolicy.Enrolment, TokenScopes.CisRead));
    }

    public static TheoryData<string> Protected =>
        [.. Inventory.Where(p => p.Value.Policy != AccessPolicy.Anonymous).Select(p => p.Key)];

    [Theory]
    [MemberData(nameof(Protected))]
    public async Task Every_protected_endpoint_answers_401_without_credentials(string name)
    {
        using var client = factory.CreateClient();
        var (methods, pattern, _, _) = Inventory[name];

        foreach (var method in methods.Split(','))
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), Url(pattern)) { Content = Json(method) };
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.True(response.Headers.Contains("Content-Security-Policy"), $"{name}: 401 without security headers");
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        }
    }

    [Theory]
    [MemberData(nameof(Protected))]
    public async Task Every_protected_endpoint_answers_401_to_a_malformed_bearer_token_whatever_cookie_comes_with_it(string name)
    {
        using var client = factory.CreateClient();
        var (methods, pattern, _, _) = Inventory[name];

        foreach (var method in methods.Split(','))
        {
            foreach (var authorization in MalformedTokens)
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), Url(pattern)) { Content = Json(method) };
                request.Headers.TryAddWithoutValidation("Authorization", authorization);
                request.Headers.Add("Cookie", $"{AuthSchemes.SessionCookie}={TestSecrets.CookieValue}");
                using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

                Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{name} with '{authorization}': {response.StatusCode}");
                Assert.True(response.Headers.Contains("Content-Security-Policy"), $"{name}: 401 without security headers");
                Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
                Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            }
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
        var (methods, pattern, _, _) = Inventory[name];

        using var response = await client.PostAsync(new Uri(Url(pattern), UriKind.Relative), Json(methods), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("anti-forgery", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(AnonymousStateChanging))]
    public async Task A_bearer_token_that_authenticates_nothing_does_not_spare_the_antiforgery_token(string name)
    {
        using var client = factory.CreateClient();
        var (methods, pattern, _, _) = Inventory[name];
        using var request = new HttpRequestMessage(HttpMethod.Post, Url(pattern)) { Content = Json(methods) };
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer sxt_short");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("anti-forgery", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_request_an_API_token_authenticated_needs_no_antiforgery_token_and_any_other_still_does()
    {
        using var scope = factory.Services.CreateScope();
        var reached = new object();
        var token = ApiTokenPrincipal.Create(Guid.CreateVersion7(), "owner sync", TokenScopes.All);
        var session = AuthSetup.SessionPrincipal(new GoldmarkUser { Id = Guid.NewGuid(), UserName = "pat.admin" }, [Roles.Admin]);

        foreach (var (user, refused) in new[] { (token, false), (session, true), (new ClaimsPrincipal(new ClaimsIdentity()), true) })
        {
            foreach (var method in new[] { HttpMethods.Post, HttpMethods.Put, HttpMethods.Delete })
            {
                var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider, User = user };
                http.Request.Scheme = "https"; // the anti-forgery cookie is Secure
                http.Request.Method = method;
                var result = await ApiAntiforgery.FilterAsync(new DefaultEndpointFilterInvocationContext(http), _ => ValueTask.FromResult<object?>(reached));

                if (refused)
                {
                    Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result).StatusCode);
                    Assert.Same(user, http.User); // the endpoint's principal is put back
                }
                else
                {
                    Assert.Same(reached, result);
                }
            }
        }
    }

    [Fact]
    public async Task A_token_reaches_exactly_the_endpoints_that_declare_one_of_its_scopes()
    {
        var authorization = factory.Services.GetRequiredService<IAuthorizationService>();
        var policies = factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        using var scope = factory.Services.CreateScope();
        IReadOnlyList<string>[] grants = [TokenScopes.All, [], .. TokenScopes.All.Select(s => (IReadOnlyList<string>)[s])];

        foreach (var endpoint in Endpoints().Where(e => e.Metadata.GetMetadata<EndpointAccess>()!.Policy != AccessPolicy.Anonymous))
        {
            var access = endpoint.Metadata.GetMetadata<EndpointAccess>()!;
            var policy = (await policies.GetPolicyAsync(access.Policy))!;
            foreach (var scopes in grants)
            {
                var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
                http.SetEndpoint(endpoint);
                http.Request.Headers.Authorization = "Bearer " + TestSecrets.ApiToken;
                var token = ApiTokenPrincipal.Create(Guid.CreateVersion7(), "owner sync", scopes);

                var allowed = (await authorization.AuthorizeAsync(token, http, policy)).Succeeded;

                Assert.True(allowed == (access.TokenScope is { } needed && scopes.Contains(needed)),
                    $"{endpoint.DisplayName} with [{string.Join(", ", scopes)}]: {(allowed ? "allowed" : "refused")}");
            }
        }
    }

    [Fact]
    public async Task A_session_is_judged_by_its_roles_as_before_and_never_on_a_request_that_carries_a_token()
    {
        var authorization = factory.Services.GetRequiredService<IAuthorizationService>();
        var policies = factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        using var scope = factory.Services.CreateScope();
        var expected = new Dictionary<string, string[]>
        {
            [AccessPolicy.SignedIn] = [Roles.Admin, Roles.Editor, Roles.Viewer, Roles.Auditor],
            [AccessPolicy.Admin] = [Roles.Admin],
            [AccessPolicy.AuditReader] = [Roles.Admin, Roles.Auditor],
            [AccessPolicy.CiEditor] = [Roles.Admin, Roles.Editor],
        };

        foreach (var (name, allowedRoles) in expected)
        {
            var policy = (await policies.GetPolicyAsync(name))!;
            var endpoint = Endpoints().First(e => e.Metadata.GetMetadata<EndpointAccess>()!.Policy == name);
            foreach (var role in new[] { Roles.Admin, Roles.Editor, Roles.Viewer, Roles.Auditor })
            {
                var person = AuthSetup.SessionPrincipal(new GoldmarkUser { Id = Guid.NewGuid(), UserName = "sam.rivera" }, [role]);
                var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
                http.SetEndpoint(endpoint);
                Assert.Equal(allowedRoles.Contains(role), (await authorization.AuthorizeAsync(person, http, policy)).Succeeded);

                http.Request.Headers.Authorization = "Bearer " + TestSecrets.ApiToken;
                Assert.False((await authorization.AuthorizeAsync(person, http, policy)).Succeeded, $"{name}, {role}: a session on a token request");
            }
        }
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
        Assert.False((await authorization.AuthorizeAsync(ApiTokenPrincipal.Create(Guid.CreateVersion7(), "all", TokenScopes.All), http, fallback)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(new ClaimsPrincipal(), http, fallback)).Succeeded);

        // A request that matched no endpoint is not a resource: it goes on to its 404.
        http.SetEndpoint(null);
        Assert.True((await authorization.AuthorizeAsync(new ClaimsPrincipal(), http, fallback)).Succeeded);
    }

    private List<RouteEndpoint> Endpoints()
    {
        using var client = factory.CreateClient();
        return [.. factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()];
    }

    private static string Methods(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<HttpMethodMetadata>() is { } metadata ? string.Join(',', metadata.HttpMethods) : "*";

    private static string Policy(RouteEndpoint endpoint) => endpoint.Metadata.GetMetadata<EndpointAccess>()?.Policy ?? "(none)";

    private static string? Scope(RouteEndpoint endpoint) => endpoint.Metadata.GetMetadata<EndpointAccess>()?.TokenScope;

    private static string Url(string pattern) =>
        pattern.Replace("{id:guid}", Guid.NewGuid().ToString("D"), StringComparison.Ordinal).Replace("{type}", "VirtualMachine", StringComparison.Ordinal)
            .Replace("{name}", "costCentre", StringComparison.Ordinal);

    private static StringContent? Json(string method) =>
        method is "GET" or "HEAD" or "DELETE" ? null : new StringContent("{}", Encoding.UTF8, "application/json");

    /// <summary>An endpoint convention builder that records nothing, for the argument checks of <c>RequireAccess</c>.</summary>
    private sealed class RouteGroupBuilderStub : IEndpointConventionBuilder
    {
        public void Add(Action<EndpointBuilder> convention)
        {
        }
    }
}

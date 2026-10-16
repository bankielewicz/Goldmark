using System.Net;
using Goldmark.Api.Security;
using Goldmark.Api.Tests.Infrastructure;

namespace Goldmark.Api.Tests;

public sealed class SecurityHeadersTests(GoldmarkFactory factory) : IClassFixture<GoldmarkFactory>
{
    public static TheoryData<string> Paths => new()
    {
        "/",
        "/ci/0190a7b2-0000-7000-8000-000000000000",
        "/assets/index-abc12345.js",
        "/health/live",
        "/health/ready",
        "/api/v1/cis",
        "/missing.png",
    };

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task Every_response_carries_the_browser_security_headers(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        var headers = response.Headers;
        Assert.Equal(SecurityHeadersMiddleware.ContentSecurityPolicy, Single(headers, "Content-Security-Policy"));
        Assert.Equal("nosniff", Single(headers, "X-Content-Type-Options"));
        Assert.Equal("DENY", Single(headers, "X-Frame-Options"));
        Assert.Equal("no-referrer", Single(headers, "Referrer-Policy"));
        Assert.Equal("same-origin", Single(headers, "Cross-Origin-Opener-Policy"));
        Assert.Equal("same-origin", Single(headers, "Cross-Origin-Resource-Policy"));
        Assert.Equal(SecurityHeadersMiddleware.PermissionsPolicy, Single(headers, "Permissions-Policy"));
        Assert.False(headers.Contains("Server"));
    }

    [Fact]
    public void Content_security_policy_meets_NFR_SEC_2()
    {
        var csp = SecurityHeadersMiddleware.ContentSecurityPolicy;
        Assert.StartsWith("default-src 'self';", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("require-trusted-types-for 'script'", csp, StringComparison.Ordinal);
        Assert.Contains("object-src 'none'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-inline", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-eval", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("data:", csp, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hsts_is_off_by_default_with_the_generated_self_signed_certificate()
    {
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://goldmark.example.com") });

        using var response = await client.GetAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    private static string Single(System.Net.Http.Headers.HttpResponseHeaders headers, string name) =>
        Assert.Single(headers.GetValues(name));
}

public sealed class HstsTests
{
    [Fact]
    public async Task Hsts_is_sent_when_enabled_explicitly()
    {
        await using var factory = new GoldmarkFactory();
        factory.Settings["Goldmark:Https:Hsts:Enabled"] = "true";
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://goldmark.example.com") });

        using var response = await client.GetAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal("max-age=31536000", Assert.Single(response.Headers.GetValues("Strict-Transport-Security")));
    }

    [Fact]
    public async Task Hsts_turns_on_automatically_with_an_operator_supplied_certificate()
    {
        await using var factory = new GoldmarkFactory();
        factory.Settings["Goldmark:Https:CertificatePath"] = "/etc/goldmark/tls/server.crt";
        factory.Settings["Goldmark:Https:KeyPath"] = "/etc/goldmark/tls/server.key";
        factory.Settings["Goldmark:Https:Hsts:MaxAgeDays"] = "30";
        factory.Settings["Goldmark:Https:Hsts:IncludeSubDomains"] = "true";
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://goldmark.example.com") });

        using var response = await client.GetAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal("max-age=2592000; includeSubDomains", Assert.Single(response.Headers.GetValues("Strict-Transport-Security")));
    }

    [Fact]
    public async Task Hsts_can_be_disabled_explicitly_with_an_operator_supplied_certificate()
    {
        await using var factory = new GoldmarkFactory();
        factory.Settings["Goldmark:Https:CertificatePath"] = "/etc/goldmark/tls/server.crt";
        factory.Settings["Goldmark:Https:KeyPath"] = "/etc/goldmark/tls/server.key";
        factory.Settings["Goldmark:Https:Hsts:Enabled"] = "false";
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://goldmark.example.com") });

        using var response = await client.GetAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }
}

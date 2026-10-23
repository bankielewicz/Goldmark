using System.Net;
using System.Text.RegularExpressions;
using Goldmark.Api.Hosting;
using Goldmark.Api.Security;
using Goldmark.Api.Tests.Infrastructure;

namespace Goldmark.Api.Tests;

public sealed partial class SpaHostingTests(GoldmarkFactory factory) : IClassFixture<GoldmarkFactory>
{
    [Theory]
    [InlineData("/")]
    [InlineData("/ci/0190a7b2-0000-7000-8000-000000000000")]
    [InlineData("/ci/0190a7b2-0000-7000-8000-000000000000?tab=history")]
    [InlineData("/search?q=sql01")]
    [InlineData("/impact?ci=a&ci=b")]
    public async Task Client_routes_return_the_SPA_shell_never_stored(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("<div id=\"root\"></div>", body, StringComparison.Ordinal);
        Assert.DoesNotContain(SpaShell.NoncePlaceholder, body, StringComparison.Ordinal);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        AssertNoValidators(response);
        Assert.Equal(BodyNonce(body), HeaderNonce(response));
    }

    [Fact]
    public async Task Every_shell_response_gets_a_distinct_nonce_of_at_least_128_bits()
    {
        using var client = factory.CreateClient();
        var nonces = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < 50; i++)
        {
            using var response = await client.GetAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var nonce = HeaderNonce(response);
            Assert.Equal(BodyNonce(body), nonce);
            Assert.True(Base64UrlBits(nonce) >= 128, $"nonce has {Base64UrlBits(nonce)} bits");
            Assert.True(nonces.Add(nonce), "a nonce was reused");
        }
    }

    [Theory]
    [InlineData("If-None-Match", "*")]
    [InlineData("If-None-Match", "\"abc\"")]
    [InlineData("If-Modified-Since", "Fri, 01 Jan 2100 00:00:00 GMT")]
    [InlineData("If-Match", "\"stale\"")]
    [InlineData("If-Unmodified-Since", "Thu, 01 Jan 1970 00:00:00 GMT")]
    [InlineData("Range", "bytes=0-20")]
    public async Task Conditional_and_range_requests_get_a_complete_matching_shell(string header, string value)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/ci/x", UriKind.Relative));
        request.Headers.TryAddWithoutValidation(header, value);
        if (header == "Range")
        {
            request.Headers.TryAddWithoutValidation("If-Range", "\"stale\"");
        }

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Never 304, 206 or 412: a cached body can never be paired with this response's policy.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("<div id=\"root\"></div>", body, StringComparison.Ordinal);
        Assert.Equal(BodyNonce(body), HeaderNonce(response));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        AssertNoValidators(response);
    }

    [Fact]
    public async Task HEAD_on_the_shell_sends_headers_only_and_no_store()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Head, new Uri("/", UriKind.Relative));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Matches(NonceSource(), Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
    }

    [Theory]
    [InlineData("/index.html")]
    [InlineData("/INDEX.HTML")]
    [InlineData("/index.htm")]
    [InlineData("/page.html")]
    public async Task HTML_is_never_served_as_a_static_file(string path)
    {
        File.WriteAllText(Path.Combine(factory.WebRoot, "page.html"), $"<meta nonce=\"{SpaShell.NoncePlaceholder}\">");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(SpaShell.NoncePlaceholder, body, StringComparison.Ordinal);
        Assert.Equal(SecurityHeadersMiddleware.ContentSecurityPolicy, Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
    }

    [Fact]
    public async Task A_shell_template_without_the_placeholder_fails_without_a_nonce()
    {
        await using var broken = new GoldmarkFactory();
        File.WriteAllText(Path.Combine(broken.WebRoot, "index.html"), "<!doctype html><div id=\"root\"></div>");
        using var client = broken.CreateClient();

        using var response = await client.GetAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(SecurityHeadersMiddleware.ContentSecurityPolicy, Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("nonce", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_web_root_without_a_build_returns_404_without_a_nonce()
    {
        await using var empty = new GoldmarkFactory();
        File.Delete(Path.Combine(empty.WebRoot, "index.html"));
        using var client = empty.CreateClient();

        using var response = await client.GetAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(SecurityHeadersMiddleware.ContentSecurityPolicy, Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
    }

    [Fact]
    public async Task Nonces_never_reach_the_log()
    {
        await using var logged = new GoldmarkFactory();
        using var client = logged.CreateClient();
        var nonces = new List<string>();

        foreach (var path in new[] { "/", "/ci/x?tab=history", "/search?q=a" })
        {
            using var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);
            nonces.Add(HeaderNonce(response));
        }

        await logged.Logs.WaitForAsync(events => events.Count(e => e.Properties.ContainsKey("StatusCode")) >= 3, TimeSpan.FromSeconds(5));
        var log = logged.Logs.RenderAll();
        Assert.Contains("/ci/x", log, StringComparison.Ordinal);
        Assert.All(nonces, nonce => Assert.DoesNotContain(nonce, log, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Fingerprinted_assets_are_immutable()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/assets/index-abc12345.js", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl?.ToString());
        Assert.Equal(SecurityHeadersMiddleware.ContentSecurityPolicy, Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
    }

    [Fact]
    public async Task Unfingerprinted_static_files_must_revalidate()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/favicon.svg", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
    }

    [Theory]
    [InlineData("/missing.js")]
    [InlineData("/assets/missing-12345678.js")]
    public async Task Missing_files_are_404_not_the_SPA_shell(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/v1/no-such-resource")]
    [InlineData("POST", "/api/v1/ingest/batches")]
    [InlineData("DELETE", "/api")]
    public async Task Unknown_API_routes_return_problem_details_404(string method, string path)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("<div id=\"root\">", body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_SPA_shell_only_answers_GET_and_HEAD()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(new Uri("/ci/x", UriKind.Relative), new StringContent(""), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(SecurityHeadersMiddleware.ContentSecurityPolicy, Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
    }

    internal static string HeaderNonce(HttpResponseMessage response)
    {
        var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Equal(SecurityHeadersMiddleware.ContentSecurityPolicyWithStyleNonce(NonceSource().Match(csp).Groups[1].Value), csp);
        return NonceSource().Match(csp).Groups[1].Value;
    }

    private static string BodyNonce(string body) => Assert.Single(BodyNonceAttribute().Matches(body)).Groups[1].Value;

    private static int Base64UrlBits(string nonce) => System.Buffers.Text.Base64Url.DecodeFromChars(nonce).Length * 8;

    private static void AssertNoValidators(HttpResponseMessage response)
    {
        Assert.Null(response.Headers.ETag);
        Assert.False(response.Content.Headers.Contains("Last-Modified"));
        Assert.False(response.Headers.Contains("Accept-Ranges"));
    }

    [GeneratedRegex("style-src 'self' 'nonce-([A-Za-z0-9_-]+)';")]
    private static partial Regex NonceSource();

    [GeneratedRegex("<meta property=\"csp-nonce\" nonce=\"([^\"]*)\"")]
    private static partial Regex BodyNonceAttribute();
}

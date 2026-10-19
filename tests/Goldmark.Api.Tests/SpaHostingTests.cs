using System.Net;
using Goldmark.Api.Tests.Infrastructure;

namespace Goldmark.Api.Tests;

public sealed class SpaHostingTests(GoldmarkFactory factory) : IClassFixture<GoldmarkFactory>
{
    [Theory]
    [InlineData("/")]
    [InlineData("/ci/0190a7b2-0000-7000-8000-000000000000")]
    [InlineData("/ci/0190a7b2-0000-7000-8000-000000000000?tab=history")]
    [InlineData("/search?q=sql01")]
    [InlineData("/impact?ci=a&ci=b")]
    public async Task Client_routes_return_the_SPA_shell_without_caching(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<div id=\"root\"></div>", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Fingerprinted_assets_are_immutable()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/assets/index-abc12345.js", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl?.ToString());
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
    [InlineData("GET", "/api/v1/cis")]
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
    }
}

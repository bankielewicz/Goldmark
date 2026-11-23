using System.Net;
using System.Text;
using Goldmark.Api.Auth;
using Goldmark.Api.Hosting;
using Goldmark.Api.Security;
using Goldmark.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Goldmark.Api.Tests;

/// <summary>
/// US-54's deprecation headers: an endpoint marked <c>Deprecated(since, sunset, link)</c> answers with RFC 9745's
/// <c>Deprecation</c>, RFC 8594's <c>Sunset</c> and a <c>Link</c> with <c>rel="deprecation"</c>, errors included, and
/// no other endpoint does. No v1 endpoint is deprecated yet, so the endpoints here are mapped only in this test host,
/// behind the whole Goldmark pipeline.
/// </summary>
public sealed class DeprecationTests(DeprecatedEndpointsFactory factory) : IClassFixture<DeprecatedEndpointsFactory>
{
    private const string DeprecationValue = "@1793491200"; // 2026-11-01T00:00:00Z
    private const string SunsetValue = "Sat, 01 May 2027 12:30:15 GMT";
    private const string LinkValue = "<https://goldmark.example.com/docs/api-stability#test>; rel=\"deprecation\"";

    public static TheoryData<string, string, HttpStatusCode> DeprecatedResponses => new()
    {
        { "GET", "/api/v1/test/deprecated", HttpStatusCode.OK },
        { "GET", "/api/v1/test/deprecated/signed-in", HttpStatusCode.Unauthorized },
        { "POST", "/api/v1/test/deprecated", HttpStatusCode.BadRequest }, // no anti-forgery token
        { "GET", "/api/v1/test/deprecated/not-found", HttpStatusCode.NotFound },
        { "GET", "/api/v1/test/deprecated/throws", HttpStatusCode.InternalServerError },
    };

    [Theory]
    [MemberData(nameof(DeprecatedResponses))]
    public async Task Every_response_of_a_deprecated_endpoint_carries_Deprecation_Sunset_and_Link_errors_included(string method, string path,
        HttpStatusCode status)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = method == "POST" ? new StringContent("{}", Encoding.UTF8, "application/json") : null,
        };

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(DeprecationValue, Single(response, "Deprecation"));
        Assert.Equal(SunsetValue, Single(response, "Sunset"));
        Assert.Equal(LinkValue, Single(response, "Link"));
        Assert.True(response.Headers.Contains("Content-Security-Policy"));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Without_a_sunset_or_a_link_only_the_Deprecation_header_is_sent()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/test/deprecated/since-only", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(DeprecationValue, Single(response, "Deprecation"));
        Assert.False(response.Headers.Contains("Sunset"));
        Assert.False(response.Headers.Contains("Link"));
    }

    [Theory]
    [InlineData("/api/v1/test/current")]
    [InlineData("/api/v1/cis")]
    [InlineData("/health/live")]
    [InlineData("/api/v1/no-such-endpoint")]
    public async Task An_endpoint_that_is_not_deprecated_sends_none_of_them(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.False(response.Headers.Contains("Deprecation"), path);
        Assert.False(response.Headers.Contains("Sunset"), path);
        Assert.False(response.Headers.Contains("Link"), path);
    }

    [Fact]
    public void The_metadata_is_a_public_record_that_the_OpenAPI_document_can_read()
    {
        var endpoint = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("/x"), 0);
        var conventions = new ConventionsStub();

        conventions.Deprecated(DeprecatedEndpointsFactory.Since, DeprecatedEndpointsFactory.Sunset, DeprecatedEndpointsFactory.Link);
        conventions.ApplyTo(endpoint);

        Assert.True(typeof(EndpointDeprecation).IsPublic);
        Assert.Equal(new EndpointDeprecation(DeprecatedEndpointsFactory.Since, DeprecatedEndpointsFactory.Sunset, DeprecatedEndpointsFactory.Link),
            endpoint.Build().Metadata.GetMetadata<EndpointDeprecation>());
    }

    [Fact]
    public void A_relative_link_is_sent_as_it_is_written()
    {
        IHeaderDictionary headers = new HeaderDictionary();

        DeprecationHeaders.Apply(headers, new EndpointDeprecation(DeprecatedEndpointsFactory.Since, Link: new Uri("/docs/api-stability", UriKind.Relative)));

        Assert.Equal("</docs/api-stability>; rel=\"deprecation\"", headers.Link.ToString());
    }

    [Fact]
    public void A_sunset_before_the_deprecation_is_refused_when_the_endpoint_is_mapped()
    {
        var conventions = new ConventionsStub();

        Assert.Throws<ArgumentException>(() => conventions.Deprecated(DeprecatedEndpointsFactory.Since, DeprecatedEndpointsFactory.Since.AddSeconds(-1)));
        conventions.Deprecated(DeprecatedEndpointsFactory.Since, DeprecatedEndpointsFactory.Since);
    }

    [Theory]
    [InlineData("/docs/api stability")]
    [InlineData("/docs/api-stability>; rel=\"x\"")]
    [InlineData("/docs/api-stability\r\nSet-Cookie: x=1")]
    [InlineData("")]
    public void A_link_that_could_not_be_sent_as_written_is_refused_when_the_endpoint_is_mapped(string link) =>
        Assert.Throws<ArgumentException>(() => new ConventionsStub().Deprecated(DeprecatedEndpointsFactory.Since, link: new Uri(link, UriKind.Relative)));

    private static string Single(HttpResponseMessage response, string name) => Assert.Single(response.Headers.GetValues(name));

    /// <summary>An endpoint convention builder that keeps its conventions, to apply them to a builder of the test's own.</summary>
    private sealed class ConventionsStub : IEndpointConventionBuilder
    {
        private readonly List<Action<EndpointBuilder>> _conventions = [];

        public void Add(Action<EndpointBuilder> convention) => _conventions.Add(convention);

        public void ApplyTo(EndpointBuilder endpoint) => _conventions.ForEach(c => c(endpoint));
    }
}

/// <summary>Goldmark with a few endpoints of its own under <c>/api/v1/test</c>, deprecated or not, behind the whole pipeline.</summary>
public sealed class DeprecatedEndpointsFactory : GoldmarkFactory
{
    public static readonly DateTimeOffset Since = new(2026, 11, 1, 1, 0, 0, TimeSpan.FromHours(1));

    public static readonly DateTimeOffset Sunset = new(2027, 5, 1, 14, 30, 15, TimeSpan.FromHours(2));

    public static readonly Uri Link = new("https://goldmark.example.com/docs/api-stability#test");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, TestEndpoints>());
    }

    private sealed class TestEndpoints : IStartupFilter
    {
        // The key under which UseRouting leaves the application's route builder (ASP.NET Core's EndpointRoutingApplicationBuilderExtensions).
        private const string RouteBuilderKey = "__EndpointRouteBuilder";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            var routes = app.Properties.TryGetValue(RouteBuilderKey, out var value) && value is IEndpointRouteBuilder found
                ? found
                : throw new InvalidOperationException("The application's route builder isn't where this test looks for it.");
            var test = routes.MapGroup("/api/v1/test").AddEndpointFilter(ApiAntiforgery.FilterAsync);
            test.MapGet("/deprecated", () => Results.Ok()).AllowAnonymousByDesign("A test endpoint").Deprecated(Since, Sunset, Link);
            test.MapPost("/deprecated", () => Results.Ok()).AllowAnonymousByDesign("A test endpoint").Deprecated(Since, Sunset, Link);
            test.MapGet("/deprecated/signed-in", () => Results.Ok()).RequireAccess(AccessPolicy.SignedIn).Deprecated(Since, Sunset, Link);
            test.MapGet("/deprecated/not-found", () => ApiProblems.Problem(StatusCodes.Status404NotFound, "not-found", "Nothing here."))
                .AllowAnonymousByDesign("A test endpoint").Deprecated(Since, Sunset, Link);
            test.MapGet("/deprecated/throws", IResult () => throw new InvalidOperationException("A test endpoint's failure"))
                .AllowAnonymousByDesign("A test endpoint").Deprecated(Since, Sunset, Link);
            test.MapGet("/deprecated/since-only", () => Results.Ok()).AllowAnonymousByDesign("A test endpoint").Deprecated(Since);
            test.MapGet("/current", () => Results.Ok()).AllowAnonymousByDesign("A test endpoint");
        };
    }
}

using System.Net;
using Goldmark.Api.Tests.Infrastructure;

namespace Goldmark.Api.Tests;

public sealed class HealthEndpointTests(GoldmarkFactory factory) : IClassFixture<GoldmarkFactory>
{
    [Fact]
    public async Task Liveness_is_healthy_even_when_SQL_Server_is_unreachable()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Readiness_is_unhealthy_when_SQL_Server_is_unreachable_and_discloses_nothing()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Unhealthy", body);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("POST", "/health/live")]
    [InlineData("PUT", "/health/live")]
    [InlineData("DELETE", "/health/ready")]
    [InlineData("POST", "/health/ready")]
    public async Task Health_endpoints_only_answer_GET_and_HEAD(string method, string path)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/readiness")]
    [InlineData("/health/live/extra")]
    public async Task Unknown_health_paths_are_404_not_the_SPA_shell(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("<html", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Health_endpoints_answer_HEAD()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Head, new Uri("/health/live", UriKind.Relative));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_burst_of_readiness_requests_opens_at_most_one_SQL_probe()
    {
        await using var isolated = new GoldmarkFactory();
        using var client = isolated.CreateClient();

        var responses = await Task.WhenAll(Enumerable.Range(0, 25).Select(_ =>
            client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode));
        Assert.Single(isolated.Logs.Events, e => e.EventId() == 1001);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Readiness_failure_is_logged_without_exception_text_above_debug()
    {
        await using var isolated = new GoldmarkFactory();
        using var client = isolated.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        var warning = Assert.Single(isolated.Logs.Events, e => e.EventId() == 1001);
        Assert.Equal(Serilog.Events.LogEventLevel.Warning, warning.Level);
        Assert.Null(warning.Exception);
        Assert.DoesNotContain(TestSecrets.DatabasePassword, isolated.Logs.RenderAll(), StringComparison.Ordinal);
    }
}

internal static class LogEventExtensions
{
    public static int? EventId(this Serilog.Events.LogEvent e) =>
        e.Properties.TryGetValue("EventId", out var value)
        && value is Serilog.Events.StructureValue s
        && s.Properties.FirstOrDefault(p => p.Name == "Id")?.Value is Serilog.Events.ScalarValue { Value: int id }
            ? id
            : null;
}

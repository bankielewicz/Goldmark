using System.Net.Http.Headers;
using Goldmark.Api.Tests.Infrastructure;
using Goldmark.Api.Tokens;

namespace Goldmark.Api.Tests;

/// <summary>NFR-LOG-1: JSON logs with a correlation id; no credentials, headers or cookies.</summary>
public sealed class LoggingTests
{
    [Fact]
    public async Task Requests_are_logged_as_json_with_a_trace_id_and_without_credentials()
    {
        await using var factory = new GoldmarkFactory();
        using var client = factory.CreateClient();
        // The last request's token has the real form, so the token scheme looks it up (the database is unreachable here).
        foreach (var (path, token) in new[]
        {
            ($"/?password={TestSecrets.QuerySecret}", TestSecrets.BearerToken),
            ("/health/ready", TestSecrets.BearerToken),
            ("/api/v1/tokens", TestSecrets.BearerToken),
            ("/api/v1/cis", TestSecrets.ApiToken),
        })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("Cookie", $"session={TestSecrets.CookieValue}");
            request.Headers.Add("X-Api-Key", token);
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        }

        // Request logs are written when the pipeline unwinds, which can be just after the
        // TestServer hands the response back; wait briefly for them (health probes log at Debug).
        await factory.Logs.WaitForAsync(events => events.Count(IsRequestLog) >= 3, TimeSpan.FromSeconds(5));
        var rendered = factory.Logs.RenderAll();

        var requestLog = Assert.Single(factory.Logs.Events,
            e => IsRequestLog(e) && e.Properties["RequestPath"].ToString() == "\"/\"");
        Assert.NotNull(requestLog.TraceId);
        Assert.Contains("\"@tr\":", rendered, StringComparison.Ordinal);
        Assert.Contains("\"RequestPath\":\"/\"", rendered, StringComparison.Ordinal);
        Assert.Contains(factory.Logs.Events, e => IsRequestLog(e) && e.Properties["RequestPath"].ToString() == "\"/api/v1/cis\"");

        Assert.True(ApiTokenSecret.IsWellFormed(TestSecrets.ApiToken));
        Assert.DoesNotContain(TestSecrets.QuerySecret, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(TestSecrets.BearerToken, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(TestSecrets.ApiToken, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(TestSecrets.ApiToken["sxt_".Length..], rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(TestSecrets.CookieValue, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(TestSecrets.DatabasePassword, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Cookie", rendered, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRequestLog(Serilog.Events.LogEvent e) =>
        e.MessageTemplate.Text.StartsWith("HTTP ", StringComparison.Ordinal);
}

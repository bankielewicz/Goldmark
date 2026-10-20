using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Goldmark.Api.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Goldmark.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Goldmark hosted in-process (TestServer, Production environment) against one provisioned
/// installation on the test SQL Server, as the runtime login. Restarting is a new instance with the
/// same <see cref="KeyDirectory"/> (the key ring survives, as its volume does).
/// </summary>
public sealed class GoldmarkApp(SqlServerFixture sql, Installation installation, string keyDirectory,
    IDictionary<string, string?>? overrides = null) : WebApplicationFactory<Program>
{
    public string KeyDirectory { get; } = keyDirectory;

    public LogCapture Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Goldmark:Database:Host"] = sql.Host,
            ["Goldmark:Database:Port"] = sql.Port.ToString(CultureInfo.InvariantCulture),
            ["Goldmark:Database:Name"] = installation.Database,
            ["Goldmark:Database:User"] = installation.RuntimeUser,
            ["Goldmark:Database:Password"] = installation.RuntimePassword,
            ["Goldmark:Database:ServerCertificatePath"] = sql.CertificatePath,
            ["Goldmark:Database:HostNameInCertificate"] = SqlServerFixture.CertificateHostName,
            ["Goldmark:Https:GeneratedCertificateDirectory"] = Path.Combine(KeyDirectory, "..", "tls"),
            ["Goldmark:DataProtection:KeyDirectory"] = KeyDirectory,
            ["Goldmark:Auth:RequestsPerMinute"] = "10000",
        };
        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
        builder.ConfigureServices(services => services.AddSingleton<ILogEventSink>(Logs));
    }

    /// <summary>A browser-like client: its own cookies (https, so __Host- cookies are kept) and anti-forgery handling.</summary>
    public ApiClient Client()
    {
        var cookies = new CookieContainer();
        return new ApiClient(CreateDefaultClient(new Uri("https://localhost"), new CookieHandler(cookies)), cookies);
    }

    /// <summary>The setup token this instance logged at start (waits for the database to answer).</summary>
    public async Task<string> SetupTokenAsync()
    {
        _ = Services; // starts the host, and with it SetupTokenService
        await Logs.WaitForAsync(e => e.Any(IsSetupToken), TimeSpan.FromSeconds(30));
        var token = Logs.Events.Single(IsSetupToken).Properties["SetupToken"];
        return ((ScalarValue)token).Value as string ?? throw new InvalidOperationException("No setup token.");
    }

    public static bool IsSetupToken(LogEvent e) => IsEvent(e, 3001);

    public static bool IsEvent(LogEvent e, int eventId) =>
        e.Properties.TryGetValue("EventId", out var id) && id is StructureValue s
        && s.Properties.Any(p => p.Name == "Id" && p.Value is ScalarValue { Value: int value } && value == eventId);

    private sealed class CookieHandler(CookieContainer cookies) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var header = cookies.GetCookieHeader(uri);
            if (header.Length > 0)
            {
                request.Headers.Add("Cookie", header);
            }

            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                foreach (var value in setCookies)
                {
                    cookies.SetCookies(uri, value);
                }
            }

            return response;
        }
    }
}

/// <summary>Calls the API as the SPA does: JSON bodies, a fresh anti-forgery token on every state-changing request.</summary>
public sealed class ApiClient(HttpClient http, CookieContainer cookies) : IDisposable
{
    public const string AntiforgeryHeader = "X-Goldmark-Antiforgery";

    public HttpClient Http { get; } = http;

    public CookieContainer Cookies { get; } = cookies;

    public Task<Response> GetAsync(string path) => SendAsync(HttpMethod.Get, path);

    public Task<Response> PostAsync(string path, object? body = null) => SendAsync(HttpMethod.Post, path, body ?? new { });

    public Task<Response> PutAsync(string path, object body, string? ifMatch = null) => SendAsync(HttpMethod.Put, path, body, ifMatch: ifMatch);

    public Task<Response> DeleteAsync(string path) => SendAsync(HttpMethod.Delete, path);

    /// <summary>
    /// Sends a request. As the SPA does, a request refused for its anti-forgery token is retried once
    /// with a fresh token: signing in ends the session the browser had, which the token was bound to.
    /// </summary>
    public async Task<Response> SendAsync(HttpMethod method, string path, object? body = null, bool antiforgery = true, string? ifMatch = null)
    {
        var response = await SendOnceAsync(method, path, body, antiforgery, ifMatch);
        return antiforgery && IsAntiforgeryRefusal(response) ? await SendOnceAsync(method, path, body, antiforgery, ifMatch) : response;
    }

    public static bool IsAntiforgeryRefusal(Response response) =>
        response.Status == HttpStatusCode.BadRequest && response.Text.Contains("anti-forgery", StringComparison.Ordinal);

    private async Task<Response> SendOnceAsync(HttpMethod method, string path, object? body, bool antiforgery, string? ifMatch)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (antiforgery && method != HttpMethod.Get && method != HttpMethod.Head)
        {
            request.Headers.Add(AntiforgeryHeader, await AntiforgeryTokenAsync());
        }

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        using var response = await Http.SendAsync(request, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var json = text.Length > 0 && response.Content.Headers.ContentType?.MediaType is { } type && type.Contains("json", StringComparison.Ordinal)
            ? JsonDocument.Parse(text).RootElement.Clone()
            : (JsonElement?)null;
        return new Response(response.StatusCode, json, text, response.Headers.ETag?.Tag, response.Content.Headers.ContentType?.MediaType,
            response.Headers.CacheControl?.ToString());
    }

    public async Task<string> AntiforgeryTokenAsync()
    {
        var token = await GetAsync("/api/v1/auth/antiforgery");
        Assert.True(token.Status == HttpStatusCode.OK && token.Json is not null, $"anti-forgery token: {token} ({token.ContentType})");
        return token.Json!.Value.GetProperty("token").GetString()!;
    }

    /// <summary>The Cookie header this client would send, to replay it from another client.</summary>
    public string CookieHeader() => Cookies.GetCookieHeader(new Uri("https://localhost/"));

    public void Dispose() => Http.Dispose();
}

public sealed record Response(HttpStatusCode Status, JsonElement? Json, string Text, string? ETag, string? ContentType, string? CacheControl)
{
    public string? Code => Json is { } j && j.TryGetProperty("code", out var code) ? code.GetString() : null;

    public JsonElement Body => Json ?? throw new InvalidOperationException($"No JSON body: {Status} {Text}");

    public string Value(string name) => Body.GetProperty(name).GetString()!;

    public override string ToString() => $"{(int)Status} {Text}";
}

/// <summary>
/// An authenticator app for one key: hands out codes for successive time steps, never the same step
/// twice (Goldmark refuses a reused code), waiting for the next step when the window is used up.
/// </summary>
public sealed class Authenticator(string formattedKey)
{
    private readonly byte[] _key = Base32.Decode(formattedKey.Replace(" ", "", StringComparison.Ordinal));
    private long _lastStep = long.MinValue;

    public string Key { get; } = formattedKey.Replace(" ", "", StringComparison.Ordinal);

    public async Task<string> NextCodeAsync()
    {
        while (true)
        {
            var now = Totp.TimeStep(DateTimeOffset.UtcNow);
            var step = Math.Max(_lastStep + 1, now - 1);
            if (step <= now + 1)
            {
                _lastStep = step;
                return Code(step);
            }

            await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        }
    }

    /// <summary>A code the server must refuse: the one for the last step already used.</summary>
    public string LastCode() => Code(_lastStep);

    public string Code(long step) => Totp.Compute(_key, step).ToString("D6", CultureInfo.InvariantCulture);
}

/// <summary>Collects log events and renders them as the production console sink does.</summary>
public sealed class LogCapture : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();
    private readonly RenderedCompactJsonFormatter _formatter = new();

    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);

    public IReadOnlyList<LogEvent> Events => [.. _events];

    public async Task WaitForAsync(Func<IReadOnlyList<LogEvent>, bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition(Events) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }
    }

    /// <summary>Each event rendered on its own, as one console line.</summary>
    public IReadOnlyList<string> RenderEach() => [.. _events.Select(e =>
    {
        using var writer = new StringWriter();
        _formatter.Format(e, writer);
        return writer.ToString();
    })];

    public string RenderAll()
    {
        using var writer = new StringWriter();
        foreach (var e in _events)
        {
            _formatter.Format(e, writer);
        }

        return writer.ToString();
    }
}

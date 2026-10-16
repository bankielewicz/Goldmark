using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Goldmark.Api.Tests.Infrastructure;

/// <summary>Secret values used by tests; they must never appear in logs or responses.</summary>
internal static class TestSecrets
{
    public const string DatabasePassword = "Db-Pa55word-NEVER-LOG-7f3a";
    public const string BearerToken = "sxt_test-token-NEVER-LOG-91c2";
    public const string CookieValue = "cookie-NEVER-LOG-44d0";
    public const string QuerySecret = "query-NEVER-LOG-0b7e";
}

/// <summary>
/// Hosts Goldmark in-process (TestServer) in the Production environment, with a fake SPA
/// build and a database endpoint where nothing listens, so readiness must fail.
/// </summary>
public class GoldmarkFactory : WebApplicationFactory<Program>
{
    private readonly string _root = Directory.CreateTempSubdirectory("goldmark-tests-").FullName;

    public GoldmarkFactory()
    {
        WebRoot = Path.Combine(_root, "wwwroot");
        Directory.CreateDirectory(Path.Combine(WebRoot, "assets"));
        File.WriteAllText(Path.Combine(WebRoot, "index.html"),
            "<!doctype html><html lang=\"en\"><head><script type=\"module\" src=\"/assets/index-abc12345.js\"></script></head>" +
            "<body><div id=\"root\"></div></body></html>");
        File.WriteAllText(Path.Combine(WebRoot, "assets", "index-abc12345.js"), "console.log('goldmark');");
        File.WriteAllText(Path.Combine(WebRoot, "favicon.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");

        Settings = new Dictionary<string, string?>
        {
            ["Goldmark:Database:Host"] = "127.0.0.1",
            ["Goldmark:Database:Port"] = "1",
            ["Goldmark:Database:User"] = "goldmark_app",
            ["Goldmark:Database:Password"] = TestSecrets.DatabasePassword,
            ["Goldmark:Database:ConnectTimeoutSeconds"] = "2",
            ["Goldmark:Https:GeneratedCertificateDirectory"] = Path.Combine(_root, "tls"),
        };
    }

    public string WebRoot { get; }

    /// <summary>Creates a file with the given content in this factory's temporary directory.</summary>
    public string CreateFile(string name, string content = "placeholder")
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    public Dictionary<string, string?> Settings { get; }

    public CapturingSink Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting(WebHostDefaults.WebRootKey, WebRoot);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(Settings));
        builder.ConfigureServices(services => services.AddSingleton<ILogEventSink>(Logs));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}

/// <summary>Collects log events and renders them exactly as the production console sink does.</summary>
public sealed class CapturingSink : ILogEventSink
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
            await Task.Delay(25);
        }
    }

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

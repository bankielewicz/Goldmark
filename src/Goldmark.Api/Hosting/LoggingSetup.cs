using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Goldmark.Api.Hosting;

/// <summary>
/// Structured JSON logs to stdout (NFR-LOG-1), and with <c>Goldmark:Logging:Directory</c> also to one file
/// per day in that owner-only folder (the Windows service, which has no console; DEC-043 item 4). Every
/// event carries the W3C trace id (<c>@tr</c>) as the per-request correlation id. Request logs contain
/// method, path (never the query string), status and duration; headers, cookies and bodies are never
/// logged, and no component logs connection strings or credentials. Nothing goes to the Windows event
/// log, which anyone signed in on the server can read: Serilog replaces the logger providers, the event
/// log's included.
/// </summary>
internal static class LoggingSetup
{
    public const string MinimumLevelKey = "Goldmark:Logging:MinimumLevel";
    public const string DirectoryKey = "Goldmark:Logging:Directory";
    public const string RetainedFilesKey = "Goldmark:Logging:RetainedFiles";
    public const int DefaultRetainedFiles = 31;

    /// <summary>The log folder's full path (relative to the content root), or null for stdout only.</summary>
    public static string? Directory(IConfiguration configuration, string contentRoot) =>
        configuration[DirectoryKey] is { Length: > 0 } directory ? Path.GetFullPath(directory, contentRoot) : null;

    /// <summary>Checked before the host is built: the number of files kept, and an owner-only folder.</summary>
    public static List<string> Errors(IConfiguration configuration, string contentRoot, string? serviceAccount)
    {
        var errors = new List<string>();
        if (configuration.GetValue(RetainedFilesKey, DefaultRetainedFiles) is < 1 or > 3650)
        {
            errors.Add($"{RetainedFilesKey} must be between 1 and 3650.");
        }

        if (Directory(configuration, contentRoot) is { } directory
            && Security.PrivateDirectory.Problem(DirectoryKey, directory, serviceAccount) is { } problem)
        {
            errors.Add(problem);
        }

        return errors;
    }

    public static WebApplicationBuilder AddGoldmarkLogging(this WebApplicationBuilder builder)
    {
        builder.Services.AddSerilog((services, logger) =>
        {
            var level = builder.Configuration.GetValue(MinimumLevelKey, LogEventLevel.Information);
            logger.MinimumLevel.Is(level)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.Console(new RenderedCompactJsonFormatter());
            if (Directory(builder.Configuration, builder.Environment.ContentRootPath) is { } directory)
            {
                logger.WriteTo.File(new RenderedCompactJsonFormatter(), Path.Combine(directory, "goldmark-.json"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: builder.Configuration.GetValue(RetainedFilesKey, DefaultRetainedFiles));
            }

            // Extra sinks registered in DI (used by tests to inspect log output).
            foreach (var sink in services.GetServices<ILogEventSink>())
            {
                logger.WriteTo.Sink(sink);
            }
        },
        // Each host owns its logger; the static Serilog.Log is never assigned or disposed.
        preserveStaticLogger: true);
        return builder;
    }

    private const string OriginalPathKey = "Goldmark.OriginalRequestPath";

    public static IApplicationBuilder UseGoldmarkRequestLogging(this IApplicationBuilder app)
    {
        // The SPA fallback rewrites the path to /index.html; log the path the client asked for
        // (without the query string, which may carry search terms or cursors).
        app.Use((context, next) =>
        {
            context.Items[OriginalPathKey] = context.Request.Path.Value;
            return next(context);
        });
        var hostLogger = app.ApplicationServices.GetRequiredService<Serilog.ILogger>();
        return app.UseSerilogRequestLogging(options =>
        {
            // Use this host's logger, not the static Serilog.Log (which is never configured).
            options.Logger = hostLogger;
            options.IncludeQueryInRequestPath = false;
            options.GetMessageTemplateProperties = (context, path, elapsed, status) =>
            [
                new LogEventProperty("RequestMethod", new ScalarValue(context.Request.Method)),
                new LogEventProperty("RequestPath", new ScalarValue(context.Items[OriginalPathKey] as string ?? path)),
                new LogEventProperty("StatusCode", new ScalarValue(status)),
                new LogEventProperty("Elapsed", new ScalarValue(elapsed)),
            ];
            options.GetLevel = (context, _, exception) =>
                exception is not null ? LogEventLevel.Error
                : IsHealthProbe(context) ? LogEventLevel.Debug
                : context.Response.StatusCode >= 500 ? LogEventLevel.Error
                : LogEventLevel.Information;
        });
    }

    private static bool IsHealthProbe(HttpContext context) =>
        (context.Items[OriginalPathKey] as string ?? "").StartsWith("/health/", StringComparison.Ordinal);
}

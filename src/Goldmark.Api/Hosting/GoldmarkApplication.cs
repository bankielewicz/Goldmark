using System.Security.Authentication;
using Goldmark.Api.Configuration;
using Goldmark.Api.Data;
using Goldmark.Api.Health;
using Goldmark.Api.Security;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Options;

namespace Goldmark.Api.Hosting;

internal static class GoldmarkApplication
{
    public static WebApplicationBuilder AddGoldmark(this WebApplicationBuilder builder)
    {
        builder.AddGoldmarkLogging();

        builder.Services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();
        builder.Services.AddOptions<DatabaseOptions>().BindConfiguration(DatabaseOptions.SectionName).ValidateOnStart()
            .PostConfigure<IHostEnvironment>((db, environment) =>
            {
                // The runtime login's name and password come from bootstrap-db's credential file
                // when one is configured (DEC-034); read once, at startup.
                db.CredentialFile = Rooted(db.CredentialFile, environment.ContentRootPath);
                DatabaseOptionsValidator.ApplyCredentialFile(db);
            });
        builder.Services.AddSingleton<IValidateOptions<HttpsOptions>, HttpsOptionsValidator>();
        builder.Services.AddOptions<HttpsOptions>().BindConfiguration(HttpsOptions.SectionName).ValidateOnStart()
            .PostConfigure<IHostEnvironment>((https, environment) =>
            {
                // Relative paths are relative to the content root, not the current directory.
                https.GeneratedCertificateDirectory = Rooted(https.GeneratedCertificateDirectory, environment.ContentRootPath) ?? "";
                https.CertificatePath = Rooted(https.CertificatePath, environment.ContentRootPath);
                https.KeyPath = Rooted(https.KeyPath, environment.ContentRootPath);
            });

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<SqlConnectionFactory>();
        builder.Services.AddSingleton<SpaShell>();
        builder.Services.AddGoldmarkHealthChecks();
        builder.Services.AddProblemDetails();

        builder.Services.AddOptions<HstsOptions>().Configure<IOptions<HttpsOptions>>((hsts, https) =>
        {
            hsts.MaxAge = TimeSpan.FromDays(https.Value.Hsts.MaxAgeDays);
            hsts.IncludeSubDomains = https.Value.Hsts.IncludeSubDomains;
            hsts.Preload = false;
        });

        // HTTPS only (US-01). There is no plain-HTTP listener.
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            var services = kestrel.ApplicationServices;
            var https = services.GetRequiredService<IOptions<HttpsOptions>>().Value;
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Goldmark.Tls");
            var certificate = new TlsCertificateProvider(https, services.GetRequiredService<TimeProvider>(), logger)
                .GetCertificate();
            kestrel.ListenAnyIP(https.Port, listen => listen.UseHttps(new HttpsConnectionAdapterOptions
            {
                ServerCertificate = certificate.Certificate,
                ServerCertificateChain = certificate.Chain,
                SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            }));
        });

        return builder;
    }

    private static string? Rooted(string? path, string contentRoot) =>
        string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) ? path : Path.GetFullPath(path, contentRoot);

    public static WebApplication UseGoldmark(this WebApplication app)
    {
        // Outermost, so every response, including error responses, gets the headers.
        app.UseMiddleware<SecurityHeadersMiddleware>();
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler();
        }

        if (HstsPolicy.IsEnabled(app.Services.GetRequiredService<IOptions<HttpsOptions>>().Value))
        {
            app.UseHsts();
        }

        app.UseGoldmarkRequestLogging();
        app.UseGoldmarkStaticFiles();
        app.MapGoldmarkHealthEndpoints();
        app.MapGoldmarkSpa();
        return app;
    }
}

using System.Security.Authentication;
using Goldmark.Api.Audit;
using Goldmark.Api.Auth;
using Goldmark.Api.Configuration;
using Goldmark.Api.Data;
using Goldmark.Api.Health;
using Goldmark.Api.Inventory;
using Goldmark.Api.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Options;

namespace Goldmark.Api.Hosting;

internal static class GoldmarkApplication
{
    public static WebApplicationBuilder AddGoldmark(this WebApplicationBuilder builder)
    {
        builder.AddGoldmarkLogging();
        // As a Windows service: the service lifetime (DEC-043). Elsewhere, and when started from a console, nothing.
        builder.Services.AddWindowsService(service => service.ServiceName = WindowsService.ServiceName);

        builder.Services.AddOptions<ServiceOptions>().BindConfiguration(ServiceOptions.SectionName);
        builder.Services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();
        builder.Services.AddSingleton<IValidateOptions<DatabaseOptions>, CredentialFolderValidator>();
        builder.Services.AddOptions<DatabaseOptions>().BindConfiguration(DatabaseOptions.SectionName).ValidateOnStart()
            .PostConfigure<IHostEnvironment>((db, environment) =>
            {
                // The runtime login's name and password come from bootstrap-db's credential file
                // when one is configured (DEC-034); read once, at startup. On Windows a password an
                // operator's command left plain is encrypted to this account now (DEC-043 item 10).
                db.CredentialFile = Rooted(db.CredentialFile, environment.ContentRootPath);
                DatabaseOptionsValidator.ApplyCredentialFile(db, protectPlain: OperatingSystem.IsWindows());
            });
        builder.Services.AddSingleton<IValidateOptions<HttpsOptions>, HttpsOptionsValidator>();
        builder.Services.AddSingleton<IValidateOptions<HttpsOptions>, GeneratedCertificateFolderValidator>();
        builder.Services.AddOptions<HttpsOptions>().BindConfiguration(HttpsOptions.SectionName).ValidateOnStart()
            .PostConfigure<IHostEnvironment>((https, environment) =>
            {
                // Relative paths are relative to the content root, not the current directory.
                https.GeneratedCertificateDirectory = Rooted(https.GeneratedCertificateDirectory, environment.ContentRootPath) ?? "";
                https.CertificatePath = Rooted(https.CertificatePath, environment.ContentRootPath);
                https.KeyPath = Rooted(https.KeyPath, environment.ContentRootPath);
            });

        builder.Services.AddSingleton<IValidateOptions<DataProtectionSettings>, DataProtectionSettingsValidator>();
        builder.Services.AddOptions<DataProtectionSettings>().BindConfiguration(DataProtectionSettings.SectionName).ValidateOnStart()
            .PostConfigure<IHostEnvironment>((keys, environment) => keys.KeyDirectory = Rooted(keys.KeyDirectory, environment.ContentRootPath) ?? "");
        // Data Protection keys as files in an owner-only directory from settings (DEC-038 item 5); on
        // Windows encrypted to the service account (DEC-043 item 5).
        builder.Services.AddDataProtection().SetApplicationName("Goldmark");
        builder.Services.AddOptions<KeyManagementOptions>()
            .Configure<IOptions<DataProtectionSettings>, IOptions<ServiceOptions>, ILoggerFactory>((keys, settings, service, loggers) =>
            {
                keys.XmlRepository = new PrivateKeyRingRepository(settings.Value.KeyDirectory, service.Value.Account);
                if (OperatingSystem.IsWindows())
                {
                    keys.XmlEncryptor = KeyRingEncryption.ForCurrentAccount(loggers);
                }
            });

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<SqlConnectionFactory>();
        builder.Services.AddScoped<DbSession>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<AuditTrail>();
        builder.Services.AddScoped<AuditRetention>();
        builder.Services.AddScoped<AuditLog>();
        builder.Services.AddScoped<CiTypeCatalog>();
        builder.Services.AddScoped<CiStore>();
        builder.Services.AddScoped<RelationshipStore>();
        builder.Services.AddScoped<GraphStore>();
        builder.Services.AddHostedService<AuditRetentionJob>();
        builder.Services.AddSingleton<SpaShell>();
        builder.Services.AddGoldmarkHealthChecks();
        builder.Services.AddProblemDetails();
        builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.Converters.Add(new UtcDateTimeConverter()));
        builder.Services.AddGoldmarkAuth();

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
        app.UseGoldmarkApiNoStore();
        // Static files first, then routing: existing assets are served without authentication, and a
        // request that reaches routing gets an endpoint (or none) before authorization runs.
        app.UseGoldmarkStaticFiles();
        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGoldmarkHealthEndpoints();
        app.MapGoldmarkApi();
        app.MapGoldmarkSpa();
        return app;
    }
}

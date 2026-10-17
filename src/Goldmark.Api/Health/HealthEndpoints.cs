using Goldmark.Api.Security;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Goldmark.Api.Health;

internal static class HealthEndpoints
{
    public const string ReadyTag = "ready";

    // MapHealthChecks matches every HTTP method by default; the contract is GET (and HEAD).
    private static readonly HttpMethodMetadata ReadOnlyMethods = new([HttpMethods.Get, HttpMethods.Head]);

    public static IServiceCollection AddGoldmarkHealthChecks(this IServiceCollection services)
    {
        services.AddSingleton<ReadinessGate>();
        services.AddHealthChecks()
            .AddCheck<SqlReadinessCheck>(SqlReadinessCheck.Name, tags: [ReadyTag], timeout: TimeSpan.FromSeconds(10));
        return services;
    }

    /// <summary>
    /// Health endpoints are anonymous by design (orchestrators and load balancers call them
    /// without credentials). They return only "Healthy"/"Unhealthy" with no component
    /// names, versions, host names or error text, and responses are not cacheable.
    /// </summary>
    public static IEndpointRouteBuilder MapGoldmarkHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Liveness: the process is up and serving HTTP. Independent of SQL Server by design.
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
            .WithMetadata(ReadOnlyMethods)
            .AllowAnonymousByDesign("Liveness probe for container orchestrators; no data exposed.")
            .WithDisplayName("Health: live");

        // Readiness: can serve requests that need the database.
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains(ReadyTag) })
            .WithMetadata(ReadOnlyMethods)
            .AllowAnonymousByDesign("Readiness probe for container orchestrators; status text only.")
            .WithDisplayName("Health: ready");

        return endpoints;
    }
}

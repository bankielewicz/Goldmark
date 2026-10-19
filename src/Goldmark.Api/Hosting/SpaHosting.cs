using Goldmark.Api.Security;

namespace Goldmark.Api.Hosting;

/// <summary>Serves the prebuilt browser-only SPA from wwwroot (D1).</summary>
internal static class SpaHosting
{
    private const string ImmutableAssets = "public, max-age=31536000, immutable";
    private const string Revalidate = "no-cache";

    /// <summary>Static assets. Vite fingerprints everything under /assets, so those are immutable.</summary>
    public static IApplicationBuilder UseGoldmarkStaticFiles(this IApplicationBuilder app) =>
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl =
                ctx.Context.Request.Path.StartsWithSegments("/assets") ? ImmutableAssets : Revalidate,
        });

    public static IEndpointRouteBuilder MapGoldmarkSpa(this IEndpointRouteBuilder endpoints)
    {
        // Unknown API routes get a Problem Details 404, never the SPA shell.
        endpoints.Map("/api/{**path}", () => Results.Problem(statusCode: StatusCodes.Status404NotFound))
            .AllowAnonymousByDesign("Catch-all 404 for unknown API routes; returns no data.")
            .WithDisplayName("API: not found");

        // Client-side routes (e.g. /ci/{id}, /search) return the SPA shell. Paths that look
        // like files (have an extension) are not matched, so missing assets stay 404.
        endpoints.MapFallbackToFile("{**path:nonfile}", "index.html", new StaticFileOptions
        {
            OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = Revalidate,
        })
            .WithMetadata(new HttpMethodMetadata([HttpMethods.Get, HttpMethods.Head]))
            .AllowAnonymousByDesign("SPA shell must load before sign-in; static HTML with no data.")
            .WithDisplayName("SPA: shell");

        return endpoints;
    }
}

namespace Goldmark.Api.Security;

/// <summary>
/// Adds the browser security headers required by NFR-SEC-2 to every response, including API
/// and health responses. HSTS is handled separately (see <see cref="Configuration.HstsPolicy"/>).
/// </summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    /// <summary>
    /// Strict CSP: same-origin only, no inline script or style, no eval, no framing, and
    /// Trusted Types enforced for all script sinks. <c>trusted-types 'none'</c> forbids creating
    /// any policy until the single reviewed policy module exists (NFR-SEC-9).
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self'; " +
        "img-src 'self'; " +
        "font-src 'self'; " +
        "connect-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'none'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'; " +
        "require-trusted-types-for 'script'; " +
        "trusted-types 'none'";

    public const string PermissionsPolicy =
        "camera=(), microphone=(), geolocation=(), payment=(), usb=(), serial=(), bluetooth=(), hid=()";

    public Task InvokeAsync(HttpContext context)
    {
        // Applied when the response starts, so they survive the exception handler,
        // which clears headers before writing an error response.
        context.Response.OnStarting(static state =>
        {
            var headers = ((HttpContext)state).Response.Headers;
            headers.ContentSecurityPolicy = ContentSecurityPolicy;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";
            headers["Permissions-Policy"] = PermissionsPolicy;
            return Task.CompletedTask;
        }, context);
        return next(context);
    }
}

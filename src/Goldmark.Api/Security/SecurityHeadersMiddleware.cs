using System.Text.RegularExpressions;

namespace Goldmark.Api.Security;

/// <summary>
/// Adds the browser security headers required by NFR-SEC-2 to every response, including API
/// and health responses. HSTS is handled separately (see <see cref="Configuration.HstsPolicy"/>).
/// </summary>
internal sealed partial class SecurityHeadersMiddleware(RequestDelegate next)
{
    private const string BeforeStyleSources = "default-src 'self'; script-src 'self'; ";

    private const string AfterStyleSources =
        "; img-src 'self'; " +
        "font-src 'self'; " +
        "connect-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'none'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'; " +
        "require-trusted-types-for 'script'; " +
        "trusted-types 'none'";

    /// <summary>
    /// Strict CSP: same-origin only, no inline script or style, no eval, no framing, and
    /// Trusted Types enforced for all script sinks. <c>trusted-types 'none'</c> forbids creating
    /// any policy until the single reviewed policy module exists (NFR-SEC-9). Every response
    /// except the SPA shell carries this policy.
    /// </summary>
    public const string ContentSecurityPolicy = BeforeStyleSources + "style-src 'self'" + AfterStyleSources;

    public const string PermissionsPolicy =
        "camera=(), microphone=(), geolocation=(), payment=(), usb=(), serial=(), bluetooth=(), hid=()";

    /// <summary>
    /// The SPA shell's policy: <see cref="ContentSecurityPolicy"/> plus that response's style nonce in
    /// <c>style-src</c> only, so Fluent UI's runtime style elements, which carry the nonce, may load
    /// (DEC-037). Scripts stay <c>'self'</c> only.
    /// </summary>
    public static string ContentSecurityPolicyWithStyleNonce(string nonce)
    {
        if (!NonceFormat().IsMatch(nonce))
        {
            throw new ArgumentException("A style nonce must be base64url text of at least 128 bits.", nameof(nonce));
        }

        return $"{BeforeStyleSources}style-src 'self' 'nonce-{nonce}'{AfterStyleSources}";
    }

    public Task InvokeAsync(HttpContext context)
    {
        // Applied when the response starts, so they survive the exception handler,
        // which clears headers before writing an error response.
        context.Response.OnStarting(static state =>
        {
            var context = (HttpContext)state;
            var headers = context.Response.Headers;
            // Only a successful shell response carries its nonce; an error page never does.
            var nonce = context.Features.Get<StyleNonceFeature>()?.Nonce;
            headers.ContentSecurityPolicy = nonce is not null && context.Response.StatusCode == StatusCodes.Status200OK
                ? ContentSecurityPolicyWithStyleNonce(nonce)
                : ContentSecurityPolicy;
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

    // 22 base64url characters encode 128 bits (132 bits of text).
    [GeneratedRegex("^[A-Za-z0-9_-]{22,}$", RegexOptions.CultureInvariant)]
    private static partial Regex NonceFormat();
}

/// <summary>
/// The style nonce of the SPA shell response being written (DEC-037). Set by the shell endpoint
/// immediately before it writes the body; read by <see cref="SecurityHeadersMiddleware"/>. Not a
/// record, so that formatting it (for example in a log message) never prints the nonce.
/// </summary>
internal sealed class StyleNonceFeature(string nonce)
{
    public string Nonce { get; } = nonce;
}

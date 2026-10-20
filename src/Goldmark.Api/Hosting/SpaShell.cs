using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Goldmark.Api.Security;
using Microsoft.Extensions.FileProviders;

namespace Goldmark.Api.Hosting;

/// <summary>
/// Renders the SPA shell (the built <c>index.html</c>) with a fresh style nonce on every response
/// (DEC-037). Fluent UI's style engine stamps the nonce on the <c>&lt;style&gt;</c> elements it
/// creates; the app reads it from <c>&lt;meta property="csp-nonce" nonce="…"&gt;</c> through the
/// element's <c>nonce</c> property, and the browser hides the attribute value from the DOM.
/// </summary>
internal sealed class SpaShell(IWebHostEnvironment environment)
{
    /// <summary>The placeholder the web build leaves in index.html (web/index.html).</summary>
    public const string NoncePlaceholder = "__GOLDMARK_STYLE_NONCE__";

    private const string NonceAttribute = $"nonce=\"{NoncePlaceholder}\"";

    // 256 bits from the OS CSPRNG; the CSP requires at least 128 (CSP3 §7.2).
    private const int NonceBytes = 32;

    private readonly Lazy<Template?> _template = new(() => Template.Load(environment.WebRootFileProvider));

    public static string CreateNonce() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(NonceBytes));

    /// <summary>
    /// GET and HEAD for every client route. Always a complete 200: no validators, no 304 and no
    /// ranges, so a body and a policy from different responses can never be combined, and
    /// <c>no-store</c> keeps browsers and proxies from storing either.
    /// </summary>
    public async Task WriteAsync(HttpContext context)
    {
        if (_template.Value is not { } template)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var nonce = CreateNonce();
        var body = template.Render(nonce);

        var response = context.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength = body.Length;
        response.Headers.CacheControl = "no-store";
        context.Features.Set(new StyleNonceFeature(nonce));

        if (!HttpMethods.IsHead(context.Request.Method))
        {
            await response.Body.WriteAsync(body, context.RequestAborted);
        }
    }

    private sealed class Template(byte[] beforeNonce, byte[] afterNonce)
    {
        /// <summary>Reads index.html once; null when the web root has no build.</summary>
        public static Template? Load(IFileProvider webRoot)
        {
            var file = webRoot.GetFileInfo("index.html");
            if (!file.Exists || file.IsDirectory)
            {
                return null;
            }

            string html;
            using (var reader = new StreamReader(file.CreateReadStream(), Encoding.UTF8))
            {
                html = reader.ReadToEnd();
            }

            var at = html.IndexOf(NonceAttribute, StringComparison.Ordinal);
            if (at < 0 || html.IndexOf(NoncePlaceholder, StringComparison.Ordinal) != html.LastIndexOf(NoncePlaceholder, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The SPA shell (index.html) must contain {NonceAttribute} exactly once; the web build is incomplete.");
            }

            var placeholderAt = at + "nonce=\"".Length;
            return new Template(
                Encoding.UTF8.GetBytes(html[..placeholderAt]),
                Encoding.UTF8.GetBytes(html[(placeholderAt + NoncePlaceholder.Length)..]));
        }

        public byte[] Render(string nonce)
        {
            // The nonce is base64url, so it needs no HTML escaping inside the attribute.
            var nonceBytes = Encoding.ASCII.GetBytes(nonce);
            var body = new byte[beforeNonce.Length + nonceBytes.Length + afterNonce.Length];
            beforeNonce.CopyTo(body, 0);
            nonceBytes.CopyTo(body, beforeNonce.Length);
            afterNonce.CopyTo(body, beforeNonce.Length + nonceBytes.Length);
            return body;
        }
    }
}

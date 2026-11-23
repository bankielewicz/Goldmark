using System.Globalization;
using Microsoft.Net.Http.Headers;

namespace Goldmark.Api.Hosting;

/// <summary>
/// An endpoint's deprecation (US-54): since when it is deprecated, when it stops answering if that is decided, and a page
/// that says what to use instead. Every response of the endpoint, errors included, carries RFC 9745's
/// <c>Deprecation</c>, RFC 8594's <c>Sunset</c> and a <c>Link</c> with <c>rel="deprecation"</c>
/// (<see cref="DeprecationHeaders"/>), and the OpenAPI document marks the operation deprecated from this metadata. Set it
/// with <see cref="EndpointDeprecationExtensions.Deprecated{TBuilder}"/>, which checks it.
/// </summary>
public sealed record EndpointDeprecation(DateTimeOffset Since, DateTimeOffset? Sunset = null, Uri? Link = null);

public static class EndpointDeprecationExtensions
{
    /// <summary>
    /// Marks an endpoint deprecated since <paramref name="since"/>, to stop answering at <paramref name="sunset"/> once
    /// that is decided, with <paramref name="link"/> pointing to what to use instead.
    /// </summary>
    public static TBuilder Deprecated<TBuilder>(this TBuilder builder, DateTimeOffset since, DateTimeOffset? sunset = null, Uri? link = null)
        where TBuilder : IEndpointConventionBuilder
    {
        if (sunset < since)
        {
            throw new ArgumentException("An endpoint's sunset can't come before its deprecation (RFC 9745, section 3).", nameof(sunset));
        }

        if (link is not null && DeprecationHeaders.LinkTarget(link) is null)
        {
            throw new ArgumentException("A deprecation link must be a URI that a Link header can carry as written.", nameof(link));
        }

        return builder.WithMetadata(new EndpointDeprecation(since, sunset, link));
    }
}

/// <summary>Writes an <see cref="EndpointDeprecation"/>'s headers on every response of its endpoint.</summary>
internal static class DeprecationHeaders
{
    /// <summary>
    /// Adds the headers when the response starts, so an error answered later in the pipeline (a refusal by authentication,
    /// the anti-forgery check or the rate limiter, an exception) carries them too. Runs after routing, which names the
    /// endpoint.
    /// </summary>
    public static IApplicationBuilder UseGoldmarkDeprecation(this IApplicationBuilder app) => app.Use((context, next) =>
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<EndpointDeprecation>() is { } deprecation)
        {
            context.Response.OnStarting(() =>
            {
                Apply(context.Response.Headers, deprecation);
                return Task.CompletedTask;
            });
        }

        return next(context);
    });

    /// <summary>RFC 9745's <c>@</c> and the Unix time in seconds, RFC 8594's HTTP date, and the link.</summary>
    public static void Apply(IHeaderDictionary headers, EndpointDeprecation deprecation)
    {
        headers["Deprecation"] = "@" + deprecation.Since.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        if (deprecation.Sunset is { } sunset)
        {
            headers["Sunset"] = sunset.ToUniversalTime().ToString("R", CultureInfo.InvariantCulture);
        }

        if (deprecation.Link is { } link && LinkTarget(link) is { } target)
        {
            headers.Append(HeaderNames.Link, $"<{target}>; rel=\"deprecation\"");
        }
    }

    /// <summary>The link as a <c>Link</c> header writes it, or null when it would not stay one value as written.</summary>
    public static string? LinkTarget(Uri link)
    {
        var text = link.IsAbsoluteUri ? link.AbsoluteUri : link.OriginalString;
        return text.Length > 0 && !text.Any(c => c is '<' or '>' or '"' || char.IsWhiteSpace(c) || char.IsControl(c)) ? text : null;
    }
}

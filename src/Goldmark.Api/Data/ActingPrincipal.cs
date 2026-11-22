using Goldmark.Api.Auth;
using Goldmark.Api.Tokens;

namespace Goldmark.Api.Data;

/// <summary>
/// Who the current request acts as: the signed-in person, an API token (DEC-048: a token acts as itself, never as the
/// Admin who created it), or nobody (an anonymous request, a job, a command). One answer for the audit log's actor
/// (<c>AuditTrail</c>) and for <c>ChangedBy</c> (<see cref="DbSession"/> puts it in the connection's
/// <c>SESSION_CONTEXT</c>; FR-CM-5, DEC-047), so the two can't name different principals. Slice 10's collectors add
/// sources here.
/// </summary>
internal sealed class ActingPrincipal(IHttpContextAccessor http)
{
    /// <summary>What <c>ChangedBy</c> holds for an API token: this, then the token's ID (lower-case "D" format).</summary>
    public const string TokenPrefix = "token:";

    /// <summary>The signed-in person's account ID, or null when the request has no session.</summary>
    public Guid? UserId =>
        http.HttpContext?.User is { } user && user.Identity?.AuthenticationType == AuthSchemes.Session ? user.UserId() : null;

    /// <summary>The API token's ID when a token authenticated the request, else null.</summary>
    public Guid? TokenId => http.HttpContext?.User.ApiTokenId();

    /// <summary>
    /// What <c>ChangedBy</c> holds for this principal: the account ID as text (DEC-044), <c>token:</c> and the token's ID
    /// (DEC-048), or null for nobody.
    /// </summary>
    public string? ChangedBy => UserId?.ToString("D") ?? (TokenId is { } token ? ForToken(token) : null);

    /// <summary>The <c>ChangedBy</c> of what an API token writes.</summary>
    public static string ForToken(Guid token) => TokenPrefix + token.ToString("D");

    /// <summary>The token's ID when <paramref name="changedBy"/> names an API token (<see cref="ForToken"/>), else null.</summary>
    public static Guid? TokenOf(string? changedBy) =>
        changedBy is not null && changedBy.StartsWith(TokenPrefix, StringComparison.Ordinal)
            && Guid.TryParseExact(changedBy.AsSpan(TokenPrefix.Length), "D", out var token)
            ? token
            : null;
}

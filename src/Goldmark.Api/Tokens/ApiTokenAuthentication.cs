using System.Security.Claims;
using System.Text.Encodings.Web;
using Goldmark.Api.Audit;
using Goldmark.Api.Auth;
using Goldmark.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Goldmark.Api.Tokens;

/// <summary>
/// The principal an API token authenticates as (DEC-048): the token itself, never the Admin who created it. Its
/// authentication type is <see cref="AuthSchemes.Token"/>, with the token's ID, name and scopes as claims and no
/// <c>NameIdentifier</c>, so nothing that reads a person's account ID can take a token for one.
/// </summary>
internal static class ApiTokenPrincipal
{
    public static ClaimsPrincipal Create(Guid id, string name, IEnumerable<string> scopes)
    {
        var identity = new ClaimsIdentity(AuthSchemes.Token, GoldmarkClaims.TokenName, ClaimTypes.Role);
        identity.AddClaim(new Claim(GoldmarkClaims.Token, id.ToString("D")));
        identity.AddClaim(new Claim(GoldmarkClaims.TokenName, name));
        foreach (var scope in scopes)
        {
            identity.AddClaim(new Claim(GoldmarkClaims.Scope, scope));
        }

        return new ClaimsPrincipal(identity);
    }

    /// <summary>Whether an API token authenticated this principal.</summary>
    public static bool IsApiToken(this ClaimsPrincipal principal) =>
        principal.Identity is { IsAuthenticated: true, AuthenticationType: AuthSchemes.Token };

    /// <summary>The token's ID when an API token authenticated this principal, else null.</summary>
    public static Guid? ApiTokenId(this ClaimsPrincipal principal) =>
        principal.IsApiToken() && Guid.TryParse(principal.FindFirstValue(GoldmarkClaims.Token), out var id) ? id : null;

    public static bool HasScope(this ClaimsPrincipal principal, string scope) =>
        principal.IsApiToken() && principal.HasClaim(GoldmarkClaims.Scope, scope);

    /// <summary>
    /// Whether the request presents a bearer token (DEC-048): it is then authenticated by that token alone, and its
    /// cookies are ignored. Another <c>Authorization</c> scheme, such as a proxy's Basic or Negotiate, leaves the request
    /// to the session cookie as before.
    /// </summary>
    public static bool PresentsBearerToken(this HttpRequest request)
    {
        foreach (var value in request.Headers.Authorization)
        {
            if (value is not null && (value.Equals(ApiTokenSecret.BearerScheme, StringComparison.OrdinalIgnoreCase)
                || value.StartsWith(ApiTokenSecret.BearerScheme + " ", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// The <see cref="AuthSchemes.Token"/> scheme (DEC-048): reads <c>Authorization: Bearer sxt_…</c> and finds the token by
/// the SHA-256 of its secret, if it is active now, in one statement. A malformed, unknown, expired or revoked token
/// fails, and the request is answered 401 with the Problem Details, security headers and <c>no-store</c> a session's
/// gets, plus RFC 6750's <c>WWW-Authenticate</c>; a token an endpoint's policy refuses gets 403.
/// <para>
/// Each request a token authenticates records its use (US-07, DEC-048): the last-used time at most once a minute and an
/// "API token used" audit entry at most once an hour, in a transaction of its own before the endpoint's. A failure there
/// is logged and never fails the request.
/// </para>
/// </summary>
internal sealed partial class ApiTokenHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory loggerFactory,
    UrlEncoder encoder, ApiTokenStore tokens, DbSession db, AuditTrail audit)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // The base class logs a failure's message: it never holds the header or the secret.
        if (!ApiTokenSecret.TryRead(Request.Headers.Authorization, out var secret))
        {
            return AuthenticateResult.Fail("No API token in a valid form.");
        }

        if (await tokens.FindActiveAsync(ApiTokenSecret.Hash(secret)) is not { } token)
        {
            return AuthenticateResult.Fail("Unknown, expired or revoked API token.");
        }

        await RecordUseAsync(token);
        return AuthenticateResult.Success(new AuthenticationTicket(ApiTokenPrincipal.Create(token.Id, token.Name, token.Scopes), Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        // RFC 6750 §3.1: a bearer token that was presented and failed is invalid_token; any other header names no error.
        Response.Headers.WWWAuthenticate = Request.Headers.Authorization.ToString().StartsWith(ApiTokenSecret.BearerScheme + " ", StringComparison.OrdinalIgnoreCase)
            ? "Bearer error=\"invalid_token\""
            : "Bearer";
        return AuthSetup.Problem(Context, StatusCodes.Status401Unauthorized);
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer error=\"insufficient_scope\"";
        return AuthSetup.Problem(Context, StatusCodes.Status403Forbidden);
    }

    private async Task RecordUseAsync(ActiveApiToken token)
    {
        try
        {
            await db.InTransactionAsync(async () =>
            {
                if (await tokens.TouchAsync(token.Id) == true)
                {
                    await audit.WriteAsync(AuditActions.TokenUsed, AuditActor.Token(token.Id), AuditTargetTypes.ApiToken, token.Id, new { name = token.Name });
                }

                return true;
            });
        }
        catch (SqlException e)
        {
            LogUseNotRecorded(Logger, token.Id, e.Number);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Anything else (a pool timeout, a broken connection) must not fail the request either.
            LogUseNotRecordedOther(Logger, token.Id, e.GetType().Name);
        }
    }

    [LoggerMessage(EventId = 5001, EventName = "ApiTokenUseNotRecorded", Level = LogLevel.Warning,
        Message = "API token {TokenId}: its use could not be recorded (SQL error {SqlErrorNumber}); the request goes on")]
    private static partial void LogUseNotRecorded(ILogger logger, Guid tokenId, int sqlErrorNumber);

    [LoggerMessage(EventId = 5002, EventName = "ApiTokenUseNotRecordedOther", Level = LogLevel.Warning,
        Message = "API token {TokenId}: its use could not be recorded ({ErrorType}); the request goes on")]
    private static partial void LogUseNotRecordedOther(ILogger logger, Guid tokenId, string errorType);
}

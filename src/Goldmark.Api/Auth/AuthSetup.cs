using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Goldmark.Api.Security;
using Goldmark.Api.Tokens;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Goldmark.Api.Auth;

/// <summary>
/// The three cookie schemes (a signed-in session, a pending second factor, an enrolment in progress), the API token
/// scheme (DEC-048) and the scheme that chooses between a session and a token for each request.
/// </summary>
internal static class AuthSchemes
{
    /// <summary>Signed in with password and second factor; server-side session (<see cref="SessionTicketStore"/>).</summary>
    public const string Session = "Goldmark.Session";

    /// <summary>An API token in <c>Authorization: Bearer sxt_…</c> (<see cref="ApiTokenHandler"/>).</summary>
    public const string Token = "Goldmark.Token";

    /// <summary>
    /// The default scheme and the one the signed-in policies authenticate with: a request that presents a bearer token
    /// is authenticated by the token alone, its cookies ignored; any other by the session cookie (DEC-048).
    /// </summary>
    public const string SessionOrToken = "Goldmark.SessionOrToken";

    /// <summary>Password verified, second factor not yet: accepted only by the sign-in step that asks for the code.</summary>
    public const string TwoFactor = "Goldmark.TwoFactor";

    /// <summary>
    /// First-run setup, an invitation, a two-factor reset or a new authenticator in progress: accepted
    /// only by the enrolment endpoints. Carries the steps done so far, encrypted by Data Protection.
    /// </summary>
    public const string Enrolment = "Goldmark.Enrolment";

    public const string SessionCookie = "__Host-goldmark";
    public const string TwoFactorCookie = "__Host-goldmark-2fa";
    public const string EnrolmentCookie = "__Host-goldmark-enrol";
    public const string AntiforgeryCookie = "__Host-goldmark-af";
    public const string AntiforgeryHeader = "X-Goldmark-Antiforgery";

    public static readonly TimeSpan TwoFactorLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan EnrolmentLifetime = TimeSpan.FromMinutes(30);
}

internal static class GoldmarkClaims
{
    public const string SecurityStamp = "goldmark:stamp";
    public const string DisplayName = "goldmark:display-name";

    /// <summary>The server-side session's id (hex of its key's hash), never the key itself.</summary>
    public const string Session = "goldmark:session";

    /// <summary>An API token's ID (DEC-048): a token's principal has no <c>NameIdentifier</c>, which names a person.</summary>
    public const string Token = "goldmark:token";

    /// <summary>An API token's name.</summary>
    public const string TokenName = "goldmark:token-name";

    /// <summary>One of an API token's scopes (<see cref="TokenScopes"/>), a claim each.</summary>
    public const string Scope = "goldmark:scope";
}

internal static class RateLimits
{
    /// <summary>Sign-in, setup and link requests, per client address (AuthOptions.RequestsPerMinute).</summary>
    public const string Credentials = "credentials";

    /// <summary>The answer to a request over a limit: 429, Problem Details with this code, and <c>Retry-After</c>.</summary>
    public const string RejectedCode = "rate-limited";
}

internal static class AuthSetup
{
    public static IServiceCollection AddGoldmarkAuth(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<AuthOptions>, AuthOptionsValidator>();
        services.AddOptions<AuthOptions>().BindConfiguration(AuthOptions.SectionName).ValidateOnStart();

        services.AddIdentityCore<GoldmarkUser>(identity =>
            {
                // NIST SP 800-63B: length only, no composition rules (DEC-038 item 6); the maximum
                // and the common-password list are PasswordPolicy's.
                identity.Password.RequiredLength = PasswordPolicy.MinimumLength;
                identity.Password.RequireDigit = false;
                identity.Password.RequireLowercase = false;
                identity.Password.RequireUppercase = false;
                identity.Password.RequireNonAlphanumeric = false;
                identity.Password.RequiredUniqueChars = 1;
                identity.Lockout.AllowedForNewUsers = true;
                identity.User.AllowedUserNameCharacters = UserNames.AllowedCharacters;
                identity.User.RequireUniqueEmail = false;
                identity.Tokens.AuthenticatorTokenProvider = TotpTokenProvider.Name;
            })
            .AddUserStore<UserStore>()
            .AddUserManager<GoldmarkUserManager>()
            .AddTokenProvider<TotpTokenProvider>(TotpTokenProvider.Name)
            .AddPasswordValidator<PasswordPolicy>()
            .AddUserValidator<UserNames>();
        services.AddOptions<IdentityOptions>().Configure<IOptions<AuthOptions>>((identity, auth) =>
        {
            identity.Lockout.MaxFailedAccessAttempts = auth.Value.LockoutThreshold;
            identity.Lockout.DefaultLockoutTimeSpan = auth.Value.Lockout;
        });
        services.AddScoped(provider => (UserStore)provider.GetRequiredService<IUserStore<GoldmarkUser>>());

        services.AddScoped<LinkStore>();
        services.AddScoped<Users.UserDirectory>();
        services.AddSingleton<SetupState>();
        services.AddHostedService<SetupTokenService>();
        services.AddHostedService<RecoverAdminService>();
        services.AddSingleton<SessionTicketStore>();
        services.AddSingleton<IPostConfigureOptions<CookieAuthenticationOptions>, SessionStoreSetup>();
        services.AddSingleton<UnknownUserThrottle>();
        services.AddScoped<ApiTokenStore>();
        services.AddSingleton<IValidateOptions<ApiOptions>, ApiOptionsValidator>();
        services.AddOptions<ApiOptions>().BindConfiguration(ApiOptions.SectionName).ValidateOnStart();
        services.AddAuthentication(authentication =>
            {
                // Authenticate, challenge and forbid per request (DEC-048); only a session is ever signed in or out.
                authentication.DefaultScheme = AuthSchemes.SessionOrToken;
                authentication.DefaultSignInScheme = AuthSchemes.Session;
                authentication.DefaultSignOutScheme = AuthSchemes.Session;
            })
            .AddPolicyScheme(AuthSchemes.SessionOrToken, "Session or API token", policy => policy.ForwardDefaultSelector = context =>
                context.Request.PresentsBearerToken() ? AuthSchemes.Token : AuthSchemes.Session)
            .AddScheme<AuthenticationSchemeOptions, ApiTokenHandler>(AuthSchemes.Token, "API token", configureOptions: null)
            .AddCookie(AuthSchemes.Session, cookie =>
            {
                Configure(cookie, AuthSchemes.SessionCookie);
                // The server-side session decides idle and absolute expiry (SessionTicketStore).
                cookie.SlidingExpiration = false;
                cookie.Events.OnValidatePrincipal = SessionValidation.ValidateAsync;
            })
            .AddCookie(AuthSchemes.TwoFactor, cookie =>
            {
                Configure(cookie, AuthSchemes.TwoFactorCookie);
                cookie.ExpireTimeSpan = AuthSchemes.TwoFactorLifetime;
                cookie.SlidingExpiration = false;
            })
            .AddCookie(AuthSchemes.Enrolment, cookie =>
            {
                Configure(cookie, AuthSchemes.EnrolmentCookie);
                cookie.ExpireTimeSpan = AuthSchemes.EnrolmentLifetime;
                cookie.SlidingExpiration = false;
            });
        services.AddOptions<CookieAuthenticationOptions>(AuthSchemes.Session).Configure<IOptions<AuthOptions>>((cookie, auth) =>
            cookie.ExpireTimeSpan = auth.Value.SessionIdle);

        // Deny by default (US-04, M6): an endpoint without a policy rejects everyone, signed in or not. A session is
        // judged by its roles, an API token by the endpoint's scope alone (EndpointAccessRequirement; DEC-048).
        services.AddSingleton<IAuthorizationHandler, DenyUnclassifiedEndpoints>();
        services.AddSingleton<IAuthorizationHandler, EndpointAccessHandler>();
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder(AuthSchemes.SessionOrToken).AddRequirements(new DenyUnclassifiedEndpoints()).Build())
            .AddPolicy(AccessPolicy.SignedIn, p => SessionOrToken(p))
            .AddPolicy(AccessPolicy.Admin, p => SessionOrToken(p, Roles.Admin))
            .AddPolicy(AccessPolicy.AuditReader, p => SessionOrToken(p, Roles.Admin, Roles.Auditor))
            .AddPolicy(AccessPolicy.CiEditor, p => SessionOrToken(p, Roles.Admin, Roles.Editor))
            .AddPolicy(AccessPolicy.TwoFactorPending, p => p.AddAuthenticationSchemes(AuthSchemes.TwoFactor).RequireAuthenticatedUser()
                .AddRequirements(new EndpointAccessRequirement([])))
            .AddPolicy(AccessPolicy.Enrolment, p => p.AddAuthenticationSchemes(AuthSchemes.Enrolment).RequireAuthenticatedUser()
                .AddRequirements(new EndpointAccessRequirement([])));

        // Anti-forgery on every state-changing API request (NFR-SEC-2): the SPA sends the token from
        // GET /api/v1/auth/antiforgery in a header that a cross-site form cannot set.
        services.AddAntiforgery(antiforgery =>
        {
            antiforgery.HeaderName = AuthSchemes.AntiforgeryHeader;
            antiforgery.FormFieldName = "__goldmark_antiforgery";
            antiforgery.Cookie.Name = AuthSchemes.AntiforgeryCookie;
            antiforgery.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            antiforgery.Cookie.SameSite = SameSiteMode.Strict;
            antiforgery.Cookie.Path = "/";
        });

        // Runs after authentication (GoldmarkApplication), so a token's requests count against the token.
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = RejectAsync;
            limiter.AddPolicy(RateLimits.Credentials, context =>
            {
                var perMinute = context.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value.RequestsPerMinute;
                return RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
            });
            // Per API token, on every request it authenticates, whatever the endpoint (PRD §5.7, DEC-048).
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (context.User.ApiTokenId() is not { } token)
                {
                    return RateLimitPartition.GetNoLimiter("");
                }

                var perMinute = context.RequestServices.GetRequiredService<IOptions<ApiOptions>>().Value.TokenRequestsPerMinute;
                return RateLimitPartition.GetFixedWindowLimiter(token.ToString("D"),
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
            });
        });

        return services;
    }

    /// <summary>
    /// A signed-in policy (DEC-048): authenticated by a session or an API token, whichever the request presents, and then
    /// judged by <see cref="EndpointAccessRequirement"/>: a session by <paramref name="roles"/> (any role when none),
    /// a token by the endpoint's scope.
    /// </summary>
    private static void SessionOrToken(AuthorizationPolicyBuilder policy, params string[] roles) =>
        policy.AddAuthenticationSchemes(AuthSchemes.SessionOrToken).RequireAuthenticatedUser().AddRequirements(new EndpointAccessRequirement(roles));

    /// <summary>429 as Problem Details with <see cref="RateLimits.RejectedCode"/>, and <c>Retry-After</c> when the limiter says.</summary>
    private static async ValueTask RejectAsync(OnRejectedContext rejected, CancellationToken cancellationToken)
    {
        var context = rejected.HttpContext;
        if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many requests. Try again later.",
                Extensions = { ["code"] = RateLimits.RejectedCode },
            },
        });
    }

    private static void Configure(CookieAuthenticationOptions cookie, string name)
    {
        // __Host- cookies: Secure, Path=/, no Domain, so no other host or path can set or read them.
        cookie.Cookie.Name = name;
        cookie.Cookie.HttpOnly = true;
        cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        cookie.Cookie.SameSite = SameSiteMode.Strict;
        cookie.Cookie.Path = "/";
        cookie.Cookie.IsEssential = true;
        // An API: answer 401/403 with Problem Details instead of redirecting to a login page.
        cookie.Events.OnRedirectToLogin = context => Problem(context.HttpContext, StatusCodes.Status401Unauthorized);
        cookie.Events.OnRedirectToAccessDenied = context => Problem(context.HttpContext, StatusCodes.Status403Forbidden);
        cookie.Events.OnRedirectToLogout = context => Task.CompletedTask;
        cookie.Events.OnRedirectToReturnUrl = context => Task.CompletedTask;
    }

    /// <summary>The Problem Details answer to a request that is unauthenticated (401) or not allowed (403), for every scheme.</summary>
    internal static Task Problem(HttpContext context, int status)
    {
        context.Response.StatusCode = status;
        return context.RequestServices.GetRequiredService<IProblemDetailsService>()
            .WriteAsync(new ProblemDetailsContext { HttpContext = context }).AsTask();
    }

    /// <summary>The principal of a signed-in session, rebuilt from the database on every request.</summary>
    public static ClaimsPrincipal SessionPrincipal(GoldmarkUser user, IEnumerable<string> roles, string? sessionId = null)
    {
        var identity = new ClaimsIdentity(AuthSchemes.Session, ClaimTypes.Name, ClaimTypes.Role);
        if (sessionId is not null)
        {
            identity.AddClaim(new Claim(GoldmarkClaims.Session, sessionId));
        }

        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString("D")));
        identity.AddClaim(new Claim(ClaimTypes.Name, user.UserName));
        identity.AddClaim(new Claim(GoldmarkClaims.DisplayName, user.DisplayName));
        identity.AddClaim(new Claim(GoldmarkClaims.SecurityStamp, user.SecurityStamp));
        foreach (var role in roles)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        return new ClaimsPrincipal(identity);
    }

    /// <summary>What a session stores: who and which security stamp; roles are read per request.</summary>
    public static ClaimsPrincipal StoredSessionPrincipal(GoldmarkUser user)
    {
        var identity = new ClaimsIdentity(AuthSchemes.Session, ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString("D")));
        identity.AddClaim(new Claim(GoldmarkClaims.SecurityStamp, user.SecurityStamp));
        return new ClaimsPrincipal(identity);
    }

    public static Guid? UserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static string? ClientAddress(this HttpContext context) =>
        context.Connection.RemoteIpAddress is { } address
            ? (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString()
            : null;
}

/// <summary>
/// The fallback policy's requirement (deny by default, US-04, M6): every application endpoint that
/// declares no policy is refused, to everyone. A request that matched no endpoint (it ends in 404) or
/// only routing's own "method not allowed" answer (405) reaches no resource, so it passes on to that
/// answer instead of becoming a 401.
/// </summary>
internal sealed class DenyUnclassifiedEndpoints : AuthorizationHandler<DenyUnclassifiedEndpoints>, IAuthorizationRequirement
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, DenyUnclassifiedEndpoints requirement)
    {
        if (context.Resource is HttpContext http && IsRoutingAnswer(http))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    private static bool IsRoutingAnswer(HttpContext http) =>
        http.GetEndpoint() is not { } endpoint || !http.RequestServices.GetRequiredService<EndpointDataSource>().Endpoints.Contains(endpoint);
}

/// <summary>
/// The access model's last word on a signed-in policy (DEC-048). A session is judged as it always was: by
/// <see cref="AllowedRoles"/> (any of them; none listed means any role). An API token never by roles: it passes only
/// where the endpoint declares a scope (<see cref="EndpointAccess.TokenScope"/>) and the token has it. A request that
/// presents a bearer token is a token's request, so no session passes on it, whatever cookie came with it.
/// </summary>
internal sealed record EndpointAccessRequirement(IReadOnlyList<string> AllowedRoles) : IAuthorizationRequirement;

internal sealed class EndpointAccessHandler : AuthorizationHandler<EndpointAccessRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, EndpointAccessRequirement requirement)
    {
        var http = context.Resource as HttpContext;
        var tokenRequest = http?.Request.PresentsBearerToken() == true;
        var allowed = context.User.IsApiToken() || tokenRequest
            ? http?.GetEndpoint()?.Metadata.GetMetadata<EndpointAccess>()?.TokenScope is { } scope && context.User.HasScope(scope)
            : requirement.AllowedRoles.Count == 0 || requirement.AllowedRoles.Any(context.User.IsInRole);
        if (allowed)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Runs on every request with a session cookie, after the server-side session was found: the person
/// must still exist, be enabled, not be locked by an administrator, have two-factor set up and hold
/// the same security stamp (a reset, a new authenticator or a new password changes it). Otherwise the
/// session ends. Roles are read fresh, so a role change applies at the next request. An automatic
/// lockout does not end a session: anyone can cause one by failing to sign in as that person.
/// </summary>
internal static class SessionValidation
{
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var users = context.HttpContext.RequestServices.GetRequiredService<GoldmarkUserManager>();
        var id = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = context.Principal?.FindFirstValue(GoldmarkClaims.SecurityStamp);
        var user = id is null ? null : await users.FindByIdAsync(id);
        if (user is null || user.IsDisabled || user.IsLockedByAdministrator || !user.HasAuthenticator || user.SecurityStamp != stamp)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(AuthSchemes.Session);
            return;
        }

        context.ReplacePrincipal(AuthSetup.SessionPrincipal(user, await users.GetRolesAsync(user),
            context.Principal!.FindFirstValue(GoldmarkClaims.Session)));
    }
}

/// <summary>
/// Anti-forgery for the API (NFR-SEC-2): every POST, PUT, PATCH and DELETE under <c>/api/v1</c> needs
/// the request token in <see cref="AuthSchemes.AntiforgeryHeader"/>. Tokens are bound to the
/// signed-in session's principal, or to no one before sign-in, whichever scheme the endpoint itself
/// authenticates with, so the token the SPA fetched is the one checked. A request an API token
/// authenticated needs none (DEC-048): NFR-SEC-2 asks for it on cookie-authenticated changes, and a
/// browser never sends an <c>Authorization</c> header across sites by itself.
/// </summary>
internal static class ApiAntiforgery
{
    public static async ValueTask<object?> FilterAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var context = invocation.HttpContext;
        if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method) || context.User.IsApiToken())
        {
            return await next(invocation);
        }

        var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
        var endpointUser = context.User;
        context.User = await SessionUserAsync(context);
        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Missing or invalid anti-forgery token.");
        }
        finally
        {
            context.User = endpointUser;
        }

        return await next(invocation);
    }

    /// <summary>Issues tokens for the same principal <see cref="FilterAsync"/> validates against.</summary>
    public static async Task<AntiforgeryTokenSet> IssueAsync(HttpContext context, IAntiforgery antiforgery)
    {
        var endpointUser = context.User;
        context.User = await SessionUserAsync(context);
        try
        {
            return antiforgery.GetAndStoreTokens(context);
        }
        finally
        {
            context.User = endpointUser;
        }
    }

    private static async Task<ClaimsPrincipal> SessionUserAsync(HttpContext context)
    {
        var session = await context.AuthenticateAsync(AuthSchemes.Session);
        return session.Succeeded ? session.Principal : new ClaimsPrincipal(new ClaimsIdentity());
    }
}

/// <summary>
/// User names: 1 to 64 letters, digits, '.', '_' or '-' (the column holds 64), unique without regard
/// to case (Identity's normalizer and the unique index).
/// </summary>
internal sealed class UserNames : IUserValidator<GoldmarkUser>
{
    public const string AllowedCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-";
    public const int MaximumLength = 64;
    public const int MaximumDisplayNameLength = 200;

    public Task<IdentityResult> ValidateAsync(UserManager<GoldmarkUser> manager, GoldmarkUser user)
    {
        var errors = new List<IdentityError>();
        if (user.UserName.Length > MaximumLength)
        {
            errors.Add(new IdentityError { Code = "UserNameTooLong", Description = $"User names are at most {MaximumLength} characters." });
        }

        if (!IsValidDisplayName(user.DisplayName))
        {
            errors.Add(new IdentityError { Code = "InvalidDisplayName", Description = $"Display names are 1 to {MaximumDisplayNameLength} characters." });
        }

        return Task.FromResult(errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]));
    }

    public static bool IsValidDisplayName(string? name) =>
        name is { Length: > 0 and <= MaximumDisplayNameLength } && name.Trim().Length == name.Length && !name.Any(char.IsControl);
}

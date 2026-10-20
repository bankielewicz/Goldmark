using System.Security.Claims;
using Goldmark.Api.Audit;
using Goldmark.Api.Data;
using Goldmark.Api.Hosting;
using Goldmark.Api.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Goldmark.Api.Auth;

/// <summary>
/// Sign-in with password and second factor, sign-out, and the session state the SPA starts from
/// (screen 1; US-03). Identity's user manager checks passwords, counts failures and locks accounts;
/// the steps are Goldmark's own so that every outcome writes an audit row and a wrong recovery code
/// counts toward lockout (owner, DEC-040). Answers never reveal whether an account exists: unknown,
/// disabled and not-yet-set-up accounts fail like a wrong password, including the lockout.
/// </summary>
internal static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth");

        // Two parameters on purpose: a lambda taking only HttpContext binds as a RequestDelegate and its result is dropped.
        auth.MapGet("/antiforgery", async (HttpContext context, IAntiforgery antiforgery) =>
                Results.Ok(new { token = (await ApiAntiforgery.IssueAsync(context, antiforgery)).RequestToken }))
            .AllowAnonymousByDesign("The SPA needs an anti-forgery token before it can sign in; the token is bound to the caller.")
            .WithDisplayName("Auth: anti-forgery token");

        auth.MapGet("/session", SessionAsync)
            .AllowAnonymousByDesign("The SPA must know whether to show sign-in or setup; returns only the caller's own state.")
            .WithDisplayName("Auth: session");

        auth.MapPost("/sign-in", SignInAsync)
            .AllowAnonymousByDesign("Sign-in step 1 (password); rate limited per client address.")
            .RequireRateLimiting(RateLimits.Credentials)
            .WithDisplayName("Auth: sign in");

        auth.MapPost("/sign-in/two-factor", SecondFactorAsync)
            .RequireAccess(AccessPolicy.TwoFactorPending)
            .WithMetadata(new StartsNewSession())
            .RequireRateLimiting(RateLimits.Credentials)
            .WithDisplayName("Auth: sign in, second factor");

        auth.MapPost("/sign-out", SignOutAsync)
            .RequireAccess(AccessPolicy.SignedIn)
            .WithDisplayName("Auth: sign out");

        return auth;
    }

    internal sealed record SignInRequest(string? UserName, string? Password);

    internal sealed record SecondFactorRequest(string? Code, string? RecoveryCode);

    internal sealed record SessionUser(Guid Id, string UserName, string DisplayName, IReadOnlyList<string> Roles);

    private static async Task<IResult> SessionAsync(HttpContext context, SetupState setup)
    {
        var session = await context.AuthenticateAsync(AuthSchemes.Session);
        var user = session.Succeeded ? Describe(session.Principal) : null;
        return Results.Ok(new { setup = setup.Status.ToString().ToLowerInvariant(), user });
    }

    private static async Task<IResult> SignInAsync(SignInRequest request, HttpContext context, GoldmarkUserManager users,
        UnknownUserThrottle throttle, DbSession db, AuditTrail audit, TimeProvider time, IOptions<AuthOptions> options)
    {
        var name = request.UserName?.Trim() ?? "";
        var password = request.Password ?? "";
        if (name.Length is 0 or > UserNames.MaximumLength * 4 || password.Length > PasswordPolicy.MaximumLength * 8)
        {
            return InvalidCredentials();
        }

        var normalized = users.NormalizeName(name);
        var user = await users.FindByNameAsync(name);
        var now = time.GetUtcNow();

        // No usable account, or one locked by an administrator: fail exactly like a wrong password,
        // with the same lockout, so the answer does not say whether the account exists.
        if (user is null || user.IsDisabled || user.IsPending || user.IsLockedByAdministrator)
        {
            if (throttle.IsLockedOut(normalized))
            {
                await audit.WriteAsync(AuditActions.SignInFailed, AuditActor.Anonymous, "User", user?.Id, new { reason = "locked-out" });
                return Locked(options.Value);
            }

            var passwordMatches = VerifyOrDummy(users, user, password);
            if (passwordMatches && user is { IsLockedByAdministrator: true, IsDisabled: false })
            {
                // Only someone who knows the password learns that an administrator locked the account.
                await audit.WriteAsync(AuditActions.SignInFailed, AuditActor.Anonymous, "User", user.Id, new { reason = "locked-by-administrator" });
                return ApiProblems.Problem(StatusCodes.Status403Forbidden, "locked-by-administrator",
                    "This account is locked. Ask an administrator to unlock it.");
            }

            var lockedNow = throttle.RecordFailure(normalized);
            await audit.WriteAsync(AuditActions.SignInFailed, AuditActor.Anonymous, "User", user?.Id,
                new { reason = user is null ? "unknown-user" : user.IsDisabled ? "disabled" : user.IsPending ? "not-set-up" : "password" });
            return lockedNow ? Locked(options.Value) : InvalidCredentials();
        }

        if (await users.IsLockedOutAsync(user))
        {
            await audit.WriteAsync(AuditActions.SignInFailed, AuditActor.Anonymous, "User", user.Id, new { reason = "locked-out" });
            return Locked(options.Value);
        }

        if (!await users.CheckPasswordAsync(user, password))
        {
            return await db.InTransactionAsync(() => FailAsync(users, audit, user, "password", options.Value, InvalidCredentials));
        }

        if (!user.HasAuthenticator)
        {
            // Two-factor was reset by an administrator (owner, DEC-040): point to the new link.
            await audit.WriteAsync(AuditActions.SignInFailed, AuditActor.Anonymous, "User", user.Id, new { reason = "two-factor-reset" });
            return ApiProblems.Problem(StatusCodes.Status403Forbidden, "two-factor-reset",
                "Two-factor was reset for this account. Open the link your administrator gave you.");
        }

        var pending = new ClaimsIdentity(AuthSchemes.TwoFactor, ClaimTypes.Name, ClaimTypes.Role);
        pending.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString("D")));
        pending.AddClaim(new Claim(GoldmarkClaims.SecurityStamp, user.SecurityStamp));
        await context.SignInAsync(AuthSchemes.TwoFactor, new ClaimsPrincipal(pending));
        return Results.Ok(new { next = "two-factor" });
    }

    private static async Task<IResult> SecondFactorAsync(SecondFactorRequest request, HttpContext context, GoldmarkUserManager users,
        DbSession db, AuditTrail audit, IOptions<AuthOptions> options)
    {
        var user = await PendingUserAsync(context, users);
        if (user is null)
        {
            await context.SignOutAsync(AuthSchemes.TwoFactor);
            return ApiProblems.Problem(StatusCodes.Status401Unauthorized, "sign-in-expired", "Sign in again.");
        }

        if (await users.IsLockedOutAsync(user))
        {
            await context.SignOutAsync(AuthSchemes.TwoFactor);
            await audit.WriteAsync(AuditActions.SignInFailed, AuditActor.Anonymous, "User", user.Id, new { reason = "locked-out" });
            return Locked(options.Value);
        }

        var usesRecoveryCode = string.IsNullOrEmpty(request.Code) && !string.IsNullOrEmpty(request.RecoveryCode);
        var outcome = await db.InTransactionAsync<IResult?>(async () =>
        {
            var accepted = usesRecoveryCode
                ? (await users.RedeemTwoFactorRecoveryCodeAsync(user, request.RecoveryCode!)).Succeeded
                : await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, request.Code ?? "");
            if (!accepted)
            {
                return await FailAsync(users, audit, user, usesRecoveryCode ? "recovery-code" : "authenticator-code", options.Value,
                    () => usesRecoveryCode
                        ? ApiProblems.Problem(StatusCodes.Status401Unauthorized, "invalid-recovery-code",
                            "That recovery code didn't work, or it was already used.")
                        : ApiProblems.Problem(StatusCodes.Status401Unauthorized, "invalid-code",
                            "That code didn't work. Codes change every 30 seconds."));
            }

            await users.ResetAccessFailedCountAsync(user);
            if (usesRecoveryCode)
            {
                await audit.WriteAsync(AuditActions.RecoveryCodeUsed, AuditActor.User(user.Id), "User", user.Id,
                    new { remaining = await users.CountRecoveryCodesAsync(user) });
            }

            await audit.WriteAsync(AuditActions.SignInSucceeded, AuditActor.User(user.Id), "User", user.Id,
                new { method = usesRecoveryCode ? "recovery-code" : "authenticator" });
            return null;
        });
        if (outcome is not null)
        {
            return outcome;
        }

        await context.SignOutAsync(AuthSchemes.TwoFactor);
        return Results.Ok(new { user = await StartSessionAsync(context, users, user) });
    }

    private static async Task<IResult> SignOutAsync(HttpContext context, AuditTrail audit)
    {
        await audit.WriteAsync(AuditActions.SignedOut);
        await context.SignOutAsync(AuthSchemes.Session);
        return Results.NoContent();
    }

    /// <summary>Signs the person in (a new server-side session), records the time, and returns who they are.</summary>
    internal static async Task<SessionUser> StartSessionAsync(HttpContext context, GoldmarkUserManager users, GoldmarkUser user)
    {
        await users.RecordSignInAsync(user);
        await context.SignInAsync(AuthSchemes.Session, AuthSetup.StoredSessionPrincipal(user),
            new AuthenticationProperties { IsPersistent = false, AllowRefresh = false });
        return Describe(AuthSetup.SessionPrincipal(user, await users.GetRolesAsync(user)))!;
    }

    internal static SessionUser? Describe(ClaimsPrincipal principal) =>
        principal.UserId() is { } id
            ? new SessionUser(id, principal.Identity?.Name ?? "", principal.FindFirstValue(GoldmarkClaims.DisplayName) ?? "",
                [.. principal.FindAll(ClaimTypes.Role).Select(c => c.Value)])
            : null;

    /// <summary>
    /// Counts a failed password or code (Identity locks the account at the threshold) and audits it,
    /// inside the caller's transaction.
    /// </summary>
    internal static async Task<IResult> FailAsync(GoldmarkUserManager users, AuditTrail audit, GoldmarkUser user, string reason,
        AuthOptions options, Func<IResult> notLocked, AuditActor? actor = null, string action = AuditActions.SignInFailed)
    {
        await users.AccessFailedAsync(user);
        await audit.WriteAsync(action, actor ?? AuditActor.Anonymous, "User", user.Id, new { reason });
        if (await users.IsLockedOutAsync(user))
        {
            await audit.WriteAsync(AuditActions.AccountLockedOut, AuditActor.System, "User", user.Id,
                new { minutes = options.LockoutMinutes });
            return Locked(options);
        }

        return notLocked();
    }

    /// <summary>The person whose password was verified in step 1, if nothing about them changed since.</summary>
    private static async Task<GoldmarkUser?> PendingUserAsync(HttpContext context, GoldmarkUserManager users)
    {
        var id = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = context.User.FindFirstValue(GoldmarkClaims.SecurityStamp);
        var user = id is null ? null : await users.FindByIdAsync(id);
        return user is { IsDisabled: false, IsPending: false, HasAuthenticator: true, IsLockedByAdministrator: false } && user.SecurityStamp == stamp
            ? user
            : null;
    }

    /// <summary>
    /// Verifies the password of an account that cannot sign in anyway, or hashes against a dummy
    /// when there is none, so every failure costs the same time.
    /// </summary>
    private static bool VerifyOrDummy(GoldmarkUserManager users, GoldmarkUser? user, string password)
    {
        if (user?.PasswordHash is { } hash)
        {
            return users.PasswordHasher.VerifyHashedPassword(user, hash, password) != PasswordVerificationResult.Failed;
        }

        _ = users.PasswordHasher.VerifyHashedPassword(DummyUser, DummyHash.Value, password);
        return false;
    }

    private static readonly GoldmarkUser DummyUser = new();

    private static readonly Lazy<string> DummyHash = new(() =>
        new PasswordHasher<GoldmarkUser>().HashPassword(DummyUser, Guid.NewGuid().ToString("N")));

    private static IResult InvalidCredentials() =>
        ApiProblems.Problem(StatusCodes.Status401Unauthorized, "invalid-credentials", "Username or password is incorrect.");

    private static IResult Locked(AuthOptions options) =>
        ApiProblems.Problem(StatusCodes.Status403Forbidden, "locked",
            $"This account is locked for {options.LockoutMinutes} minutes after {options.LockoutThreshold} failed attempts.",
            new Dictionary<string, object?> { ["minutes"] = options.LockoutMinutes, ["attempts"] = options.LockoutThreshold });
}

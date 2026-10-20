using System.Security.Claims;
using Goldmark.Api.Audit;
using Goldmark.Api.Data;
using Goldmark.Api.Hosting;
using Goldmark.Api.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Goldmark.Api.Auth;

/// <summary>
/// The signed-in person's own two-factor page (DEC-038 item 9): status, new recovery codes, and a new
/// authenticator. Each action is confirmed with the password and a current code (a recovery code is
/// accepted for a new authenticator, when the old phone is gone); wrong answers count toward lockout,
/// as at sign-in. Two-factor cannot be turned off.
/// </summary>
internal static class AccountEndpoints
{
    public static RouteGroupBuilder MapAccountEndpoints(this RouteGroupBuilder api)
    {
        var account = api.MapGroup("/account/two-factor");

        api.MapGet("/account/two-factor", StatusAsync)
            .RequireAccess(AccessPolicy.SignedIn)
            .WithDisplayName("Account: two-factor status");

        account.MapPost("/recovery-codes", NewRecoveryCodesAsync)
            .RequireAccess(AccessPolicy.SignedIn)
            .RequireRateLimiting(RateLimits.Credentials)
            .WithDisplayName("Account: new recovery codes");

        account.MapPost("/authenticator", StartNewAuthenticatorAsync)
            .RequireAccess(AccessPolicy.SignedIn)
            .RequireRateLimiting(RateLimits.Credentials)
            .WithDisplayName("Account: start a new authenticator");

        account.MapPost("/authenticator/verify", VerifyNewAuthenticatorAsync)
            .RequireAccess(AccessPolicy.SignedIn)
            .RequireRateLimiting(RateLimits.Credentials)
            .WithDisplayName("Account: verify the new authenticator");

        return account;
    }

    internal sealed record ConfirmRequest(string? Password, string? Code, string? RecoveryCode);

    private static async Task<IResult> StatusAsync(HttpContext context, GoldmarkUserManager users)
    {
        var user = await SignedInUserAsync(context, users);
        return Results.Ok(new
        {
            enabled = user.HasAuthenticator,
            setUpAt = user.AuthenticatorSetUpAt,
            recoveryCodesRemaining = await users.CountRecoveryCodesAsync(user),
            recoveryCodesTotal = EnrolmentEndpoints.RecoveryCodeCount,
        });
    }

    private static async Task<IResult> NewRecoveryCodesAsync(ConfirmRequest request, HttpContext context, GoldmarkUserManager users,
        DbSession db, AuditTrail audit, IOptions<AuthOptions> options)
    {
        var user = await SignedInUserAsync(context, users);
        return await db.InTransactionAsync(async () =>
        {
            if (await ConfirmAsync(request with { RecoveryCode = null }, user, users, audit, options.Value) is { } refused)
            {
                return refused;
            }

            var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, EnrolmentEndpoints.RecoveryCodeCount)
                ?? throw new InvalidOperationException("No recovery codes.");
            await audit.WriteAsync(AuditActions.RecoveryCodesRegenerated, "User", user.Id);
            return Results.Ok(new { recoveryCodes = codes });
        });
    }

    /// <summary>Confirms the person, then starts an enrolment for the new authenticator (see EnrolmentEndpoints).</summary>
    private static async Task<IResult> StartNewAuthenticatorAsync(ConfirmRequest request, HttpContext context, GoldmarkUserManager users,
        DbSession db, AuditTrail audit, IOptions<AuthOptions> options)
    {
        var user = await SignedInUserAsync(context, users);
        var refused = await db.InTransactionAsync(() => ConfirmAsync(request, user, users, audit, options.Value));
        if (refused is not null)
        {
            return refused;
        }

        if (!string.IsNullOrEmpty(request.RecoveryCode))
        {
            await audit.WriteAsync(AuditActions.RecoveryCodeUsed, "User", user.Id, new { remaining = await users.CountRecoveryCodesAsync(user) });
        }

        await new EnrolmentState(EnrolmentKind.ChangeAuthenticator, user.Id, SecurityStamp: user.SecurityStamp, PasswordVerified: true)
            .SaveAsync(context);
        return Results.Ok(new { next = "authenticator" });
    }

    /// <summary>
    /// Verifies a code from the new authenticator (set up through the enrolment steps) and installs it:
    /// the old authenticator stops working and every other session ends; this one continues (DEC-038
    /// item 9; ASVS 5.0 V7.4.3).
    /// </summary>
    private static async Task<IResult> VerifyNewAuthenticatorAsync(EnrolmentEndpoints.CodeRequest request, HttpContext context,
        GoldmarkUserManager users, SetupState setup, LinkStore links, DbSession db, AuditTrail audit, TimeProvider time)
    {
        var enrolment = await context.AuthenticateAsync(AuthSchemes.Enrolment);
        var current = enrolment.Succeeded ? await EnrolmentEndpoints.ResolveAsync(enrolment.Principal, users, setup, links) : null;
        if (current is not { State: { Kind: EnrolmentKind.ChangeAuthenticator, AuthenticatorKey: { } key }, User: { } user }
            || user.Id != context.User.UserId() || context.User.FindFirstValue(GoldmarkClaims.Session) is not { } session)
        {
            return ApiProblems.Problem(StatusCodes.Status409Conflict, "enrolment-expired", "Start again: confirm with your password and a code.");
        }

        if (Totp.Match(Base32.Decode(key), request.Code ?? "", time.GetUtcNow()) is not { } step)
        {
            return ApiProblems.Problem(StatusCodes.Status400BadRequest, "invalid-code", "That code didn't work. Codes change every 30 seconds.");
        }

        await db.InTransactionAsync(async () =>
        {
            if (!(await users.SetAuthenticatorAsync(user, key, step)).Succeeded)
            {
                throw new InvalidOperationException("The account changed meanwhile.");
            }

            await SessionTicketStore.EndOthersAsync(db, user.Id, session);
            await audit.WriteAsync(AuditActions.AuthenticatorChanged, "User", user.Id);
            return true;
        });

        await EnrolmentState.EndAsync(context);
        // The new security stamp goes into this session's stored ticket, so it stays valid.
        await context.SignInAsync(AuthSchemes.Session, AuthSetup.StoredSessionPrincipal(user),
            new AuthenticationProperties { IsPersistent = false, AllowRefresh = false });
        return Results.Ok(new { user = AuthEndpoints.Describe(AuthSetup.SessionPrincipal(user, await users.GetRolesAsync(user))) });
    }

    /// <summary>
    /// Checks the password and a current code (or recovery code); null when both are right. Failures
    /// count toward lockout and are audited, in the caller's transaction.
    /// </summary>
    private static async Task<IResult?> ConfirmAsync(ConfirmRequest request, GoldmarkUser user, GoldmarkUserManager users,
        AuditTrail audit, AuthOptions options)
    {
        if (await users.IsLockedOutAsync(user))
        {
            return ApiProblems.Problem(StatusCodes.Status403Forbidden, "locked",
                $"This account is locked for {options.LockoutMinutes} minutes after {options.LockoutThreshold} failed attempts.");
        }

        if (!await users.CheckPasswordAsync(user, request.Password ?? ""))
        {
            return await AuthEndpoints.FailAsync(users, audit, user, "password", options, () => ApiProblems.Field("password", "PasswordIncorrect"),
                AuditActor.User(user.Id), AuditActions.ConfirmationFailed);
        }

        var usesRecoveryCode = string.IsNullOrEmpty(request.Code) && !string.IsNullOrEmpty(request.RecoveryCode);
        var accepted = usesRecoveryCode
            ? (await users.RedeemTwoFactorRecoveryCodeAsync(user, request.RecoveryCode!)).Succeeded
            : await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, request.Code ?? "");
        if (!accepted)
        {
            return await AuthEndpoints.FailAsync(users, audit, user, usesRecoveryCode ? "recovery-code" : "authenticator-code", options,
                () => ApiProblems.Field(usesRecoveryCode ? "recoveryCode" : "code", "CodeIncorrect"), AuditActor.User(user.Id), AuditActions.ConfirmationFailed);
        }

        await users.ResetAccessFailedCountAsync(user);
        return null;
    }

    /// <summary>The session's person; the session check has already made sure they exist and may sign in.</summary>
    private static async Task<GoldmarkUser> SignedInUserAsync(HttpContext context, GoldmarkUserManager users) =>
        await users.FindByIdAsync(context.User.UserId()!.Value.ToString("D"))
            ?? throw new InvalidOperationException("The signed-in person no longer exists.");
}

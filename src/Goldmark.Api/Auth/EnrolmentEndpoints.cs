using Goldmark.Api.Audit;
using Goldmark.Api.Data;
using Goldmark.Api.Hosting;
using Goldmark.Api.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Goldmark.Api.Auth;

/// <summary>
/// First-run setup (screen 2; US-02), invitation and two-factor reset links (screen 1b; DEC-038 items 2
/// and 8), and the steps they share: choose or confirm a password, set up an authenticator, verify a code,
/// receive recovery codes. Nothing is stored until the last step, which commits in one transaction; the
/// person is then signed in. The setup page answers only while setup is not complete.
/// </summary>
internal static class EnrolmentEndpoints
{
    public const int RecoveryCodeCount = 10;
    public const string Issuer = "Goldmark";

    public static RouteGroupBuilder MapSetupEndpoints(this RouteGroupBuilder api)
    {
        var setup = api.MapGroup("/setup");

        api.MapGet("/setup", (SetupState state) => Results.Ok(new { status = state.Status.ToString().ToLowerInvariant() }))
            .AllowAnonymousByDesign("The SPA must know whether first-run setup is needed; returns the status only.")
            .WithDisplayName("Setup: status");

        setup.MapPost("/token", StartSetupAsync)
            .AllowAnonymousByDesign("First-run setup step 1: the setup token from the log is the credential; rate limited.")
            .RequireRateLimiting(RateLimits.Credentials)
            .WithDisplayName("Setup: token");

        return setup;
    }

    public static RouteGroupBuilder MapLinkEndpoints(this RouteGroupBuilder api)
    {
        var links = api.MapGroup("/links");

        links.MapPost("/check", CheckLinkAsync)
            .AllowAnonymousByDesign("An invitation or reset link's token is the credential; says only whether it still works. Rate limited.")
            .RequireRateLimiting(RateLimits.Credentials)
            .WithDisplayName("Links: check");

        links.MapPost("/redeem", RedeemLinkAsync)
            .AllowAnonymousByDesign("An invitation or reset link's token plus its separate code are the credential; 5 wrong codes revoke it. Rate limited.")
            .RequireRateLimiting(RateLimits.Credentials)
            .WithDisplayName("Links: confirm code");

        return links;
    }

    public static RouteGroupBuilder MapEnrolmentEndpoints(this RouteGroupBuilder api)
    {
        var enrolment = api.MapGroup("/enrolment");

        api.MapGet("/enrolment", DescribeAsync)
            .RequireAccess(AccessPolicy.Enrolment)
            .WithDisplayName("Enrolment: state");

        enrolment.MapPost("/account", ChooseAccountAsync)
            .RequireAccess(AccessPolicy.Enrolment)
            .RequireRateLimiting(RateLimits.Credentials)
            .WithDisplayName("Enrolment: administrator account");

        enrolment.MapPost("/password", PasswordAsync)
            .RequireAccess(AccessPolicy.Enrolment)
            .RequireRateLimiting(RateLimits.Credentials)
            .WithDisplayName("Enrolment: password");

        enrolment.MapPost("/authenticator", NewAuthenticatorAsync)
            .RequireAccess(AccessPolicy.Enrolment)
            .WithDisplayName("Enrolment: new authenticator");

        enrolment.MapGet("/authenticator.svg", QrCodeAsync)
            .RequireAccess(AccessPolicy.Enrolment)
            .WithDisplayName("Enrolment: authenticator QR code");

        enrolment.MapPost("/complete", CompleteAsync)
            .RequireAccess(AccessPolicy.Enrolment)
            .WithMetadata(new StartsNewSession())
            .RequireRateLimiting(RateLimits.Credentials)
            .WithDisplayName("Enrolment: verify and finish");

        return enrolment;
    }

    internal sealed record TokenRequest(string? Token);

    internal sealed record RedeemRequest(string? Token, string? Code);

    internal sealed record AccountRequest(string? UserName, string? DisplayName, string? Password);

    internal sealed record PasswordRequest(string? Password);

    internal sealed record CodeRequest(string? Code);

    // --- Setup ------------------------------------------------------------------------------------

    private static async Task<IResult> StartSetupAsync(TokenRequest request, HttpContext context, SetupState setup, AuditTrail audit)
    {
        if (setup.Status == SetupStatus.Complete)
        {
            return SetupComplete();
        }

        if (!setup.Verify(request.Token))
        {
            await audit.WriteAsync(AuditActions.SetupTokenRejected, AuditActor.Anonymous);
            return ApiProblems.Problem(StatusCodes.Status400BadRequest, "invalid-setup-token",
                "That token isn't valid. Use the newest one in the log; a new token is printed whenever Goldmark restarts.");
        }

        await new EnrolmentState(EnrolmentKind.Setup, SetupTokenHash: Convert.ToBase64String(setup.CurrentTokenHash!)).SaveAsync(context);
        return Results.Ok(new { next = "account" });
    }

    // --- Links --------------------------------------------------------------------------------------

    private static async Task<IResult> CheckLinkAsync(TokenRequest request, LinkStore links)
    {
        var link = LinkSecrets.IsWellFormedToken(request.Token) ? await links.FindAsync(request.Token!) : null;
        return link switch
        {
            null or { IsOpen: false } => Results.Ok(new { status = "invalid" }),
            _ when links.IsExpired(link) => Results.Ok(new { status = "expired" }),
            _ => Results.Ok(new { status = "valid", purpose = Purpose(link), triesLeft = links.TriesLeft(link) }),
        };
    }

    private static async Task<IResult> RedeemLinkAsync(RedeemRequest request, HttpContext context, LinkStore links,
        GoldmarkUserManager users, DbSession db, AuditTrail audit)
    {
        var link = LinkSecrets.IsWellFormedToken(request.Token) ? await links.FindAsync(request.Token!) : null;
        if (link is null || !link.IsOpen)
        {
            return LinkInvalid();
        }

        if (links.IsExpired(link))
        {
            return ApiProblems.Problem(StatusCodes.Status410Gone, "link-expired", "This link has expired. Ask your administrator for a new one.");
        }

        var code = LinkSecrets.NormalizeCode(request.Code);
        if (code is null || !LinkSecrets.Matches(link.CodeHash, LinkSecrets.HashCode(request.Token!, code)))
        {
            return await db.InTransactionAsync(async () =>
            {
                var left = await links.RecordWrongCodeAsync(link);
                await audit.WriteAsync(AuditActions.LinkCodeFailed, AuditActor.Anonymous, "User", link.UserId, new { purpose = link.Purpose, triesLeft = left });
                if (left is null or 0)
                {
                    await audit.WriteAsync(AuditActions.LinkInvalidated, AuditActor.System, "User", link.UserId, new { purpose = link.Purpose });
                    return ApiProblems.Problem(StatusCodes.Status410Gone, "link-invalidated",
                        "This link no longer works. Ask your administrator for a new one.");
                }

                return ApiProblems.Problem(StatusCodes.Status400BadRequest, "wrong-code", "That code doesn't match.",
                    new Dictionary<string, object?> { ["triesLeft"] = left });
            });
        }

        var user = await users.FindByIdAsync(link.UserId.ToString("D"));
        if (user is null || user.IsDisabled || (link.Purpose == LinkPurpose.Invitation) != user.IsPending)
        {
            return LinkInvalid();
        }

        var kind = link.Purpose == LinkPurpose.Invitation ? EnrolmentKind.Invitation : EnrolmentKind.TwoFactorReset;
        await new EnrolmentState(kind, user.Id, link.Id, user.SecurityStamp).SaveAsync(context);
        return Results.Ok(new
        {
            purpose = Purpose(link),
            next = "password",
            userName = user.UserName,
            displayName = user.DisplayName,
            roles = await users.GetRolesAsync(user),
        });
    }

    // --- Shared steps -------------------------------------------------------------------------------

    private static async Task<IResult> DescribeAsync(HttpContext context, GoldmarkUserManager users, SetupState setup, LinkStore links)
    {
        if (await CurrentAsync(context, users, setup, links) is not { } current)
        {
            return await EnrolmentExpiredAsync(context);
        }

        var (state, user) = current;
        IList<string> roles = user is not null ? await users.GetRolesAsync(user) : state.Kind == EnrolmentKind.Setup ? [Roles.Admin] : [];
        return Results.Ok(new
        {
            kind = KindName(state.Kind),
            next = state.NextStep,
            userName = user?.UserName ?? state.UserName,
            displayName = user?.DisplayName ?? state.DisplayName,
            roles,
        });
    }

    /// <summary>Setup step 2: the first administrator's user name, display name and password.</summary>
    private static async Task<IResult> ChooseAccountAsync(AccountRequest request, HttpContext context, GoldmarkUserManager users,
        SetupState setup, LinkStore links)
    {
        if (await CurrentAsync(context, users, setup, links) is not { State.Kind: EnrolmentKind.Setup } current)
        {
            return await EnrolmentExpiredAsync(context);
        }

        var candidate = new GoldmarkUser
        {
            Id = Guid.CreateVersion7(),
            UserName = request.UserName?.Trim() ?? "",
            DisplayName = request.DisplayName?.Trim() ?? "",
        };
        var errors = new List<IdentityError>();
        foreach (var validator in users.UserValidators)
        {
            errors.AddRange((await validator.ValidateAsync(users, candidate)).Errors);
        }

        errors.AddRange((await users.CheckPasswordPolicyAsync(candidate, request.Password ?? "")).Errors);
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        await (current.State with
        {
            UserName = candidate.UserName,
            DisplayName = candidate.DisplayName,
            PasswordHash = users.PasswordHasher.HashPassword(candidate, request.Password!),
            AuthenticatorKey = null,
        }).SaveAsync(context);
        return Results.Ok(new { next = "authenticator" });
    }

    /// <summary>
    /// An invitation's new password (checked against the policy), or, after a two-factor reset, the
    /// current password (a wrong one counts toward lockout, as at sign-in).
    /// </summary>
    private static async Task<IResult> PasswordAsync(PasswordRequest request, HttpContext context, GoldmarkUserManager users,
        SetupState setup, LinkStore links, DbSession db, AuditTrail audit, IOptions<AuthOptions> options)
    {
        if (await CurrentAsync(context, users, setup, links) is not { User: { } user } current)
        {
            return await EnrolmentExpiredAsync(context);
        }

        var password = request.Password ?? "";
        switch (current.State.Kind)
        {
            case EnrolmentKind.Invitation:
                var policy = await users.CheckPasswordPolicyAsync(user, password);
                if (!policy.Succeeded)
                {
                    return ApiProblems.Validation(policy);
                }

                await (current.State with { PasswordHash = users.PasswordHasher.HashPassword(user, password), AuthenticatorKey = null }).SaveAsync(context);
                return Results.Ok(new { next = "authenticator" });

            case EnrolmentKind.TwoFactorReset:
                if (await users.IsLockedOutAsync(user))
                {
                    return ApiProblems.Problem(StatusCodes.Status403Forbidden, "locked",
                        $"This account is locked for {options.Value.LockoutMinutes} minutes after {options.Value.LockoutThreshold} failed attempts.");
                }

                if (!await users.CheckPasswordAsync(user, password))
                {
                    var failure = await db.InTransactionAsync(() => AuthEndpoints.FailAsync(users, audit, user, "password", options.Value,
                        () => ApiProblems.Field("password", "PasswordIncorrect")));
                    return failure;
                }

                await (current.State with { PasswordVerified = true, AuthenticatorKey = null }).SaveAsync(context);
                return Results.Ok(new { next = "authenticator" });

            default:
                return ApiProblems.Problem(StatusCodes.Status409Conflict, "wrong-step", "This step does not apply.");
        }
    }

    /// <summary>Generates the new authenticator key (kept in the enrolment until a code proves it).</summary>
    private static async Task<IResult> NewAuthenticatorAsync(HttpContext context, GoldmarkUserManager users, SetupState setup, LinkStore links)
    {
        if (await CurrentAsync(context, users, setup, links) is not { } current)
        {
            return await EnrolmentExpiredAsync(context);
        }

        if (!current.State.HasPassword)
        {
            return ApiProblems.Problem(StatusCodes.Status409Conflict, "wrong-step", "Choose the password first.");
        }

        var key = users.GenerateNewAuthenticatorKey();
        await (current.State with { AuthenticatorKey = key }).SaveAsync(context);
        return Results.Ok(new { key = Totp.FormatKey(key), issuer = Issuer, userName = current.User?.UserName ?? current.State.UserName });
    }

    private static async Task<IResult> QrCodeAsync(HttpContext context, GoldmarkUserManager users, SetupState setup, LinkStore links)
    {
        if (await CurrentAsync(context, users, setup, links) is not { State.AuthenticatorKey: { } key } current)
        {
            return Results.NotFound();
        }

        var uri = Totp.ProvisioningUri(Issuer, current.User?.UserName ?? current.State.UserName ?? "", key);
        return Results.Text(QrCodeImage.Svg(uri), "image/svg+xml");
    }

    /// <summary>Verifies a code from the new authenticator and commits the whole enrolment at once.</summary>
    private static async Task<IResult> CompleteAsync(CodeRequest request, HttpContext context, GoldmarkUserManager users,
        SetupState setup, LinkStore links, DbSession db, AuditTrail audit, TimeProvider time)
    {
        if (await CurrentAsync(context, users, setup, links) is not { } current)
        {
            return await EnrolmentExpiredAsync(context);
        }

        var (state, user) = current;
        if (state.AuthenticatorKey is not { } key || !state.HasPassword)
        {
            return ApiProblems.Problem(StatusCodes.Status409Conflict, "wrong-step", "Set up the authenticator first.");
        }

        if (Totp.Match(Base32.Decode(key), request.Code ?? "", time.GetUtcNow()) is not { } step)
        {
            return ApiProblems.Problem(StatusCodes.Status400BadRequest, "invalid-code", "That code didn't work. Codes change every 30 seconds.");
        }

        if (state.Kind == EnrolmentKind.ChangeAuthenticator)
        {
            // Finished on the signed-in account page, which keeps the current session.
            return ApiProblems.Problem(StatusCodes.Status409Conflict, "wrong-step", "Finish on the two-factor page.");
        }

        IReadOnlyList<string> codes;
        AuthEndpoints.SessionUser signedIn;
        try
        {
            (signedIn, codes) = await db.InTransactionAsync(async () =>
            {
                var (person, recoveryCodes) = state.Kind == EnrolmentKind.Setup
                    ? await CompleteSetupAsync(state, key, step, users, audit)
                    : await CompleteLinkAsync(state, user!, key, step, users, links, audit);
                // The session row commits with the account: if it cannot be written, nothing is, and
                // the step can be tried again (the recovery codes were never shown).
                return (await AuthEndpoints.StartSessionAsync(context, users, person), recoveryCodes);
            });
        }
        catch (LinkClosedException)
        {
            await EnrolmentEndAsync(context);
            return LinkInvalid();
        }
        catch (SqlException ex) when (state.Kind == EnrolmentKind.Setup && ex.Number is 2627 or 2601)
        {
            // Another browser completed setup first (the setup row, or the same user name).
            setup.Complete();
            await EnrolmentEndAsync(context);
            return SetupComplete();
        }

        if (state.Kind == EnrolmentKind.Setup)
        {
            setup.Complete();
        }

        await EnrolmentEndAsync(context);
        return Results.Ok(new { user = signedIn, recoveryCodes = codes });
    }

    private static async Task<(GoldmarkUser, IReadOnlyList<string>)> CompleteSetupAsync(EnrolmentState state, string key, long step,
        GoldmarkUserManager users, AuditTrail audit)
    {
        var admin = new GoldmarkUser
        {
            Id = Guid.CreateVersion7(),
            UserName = state.UserName!,
            DisplayName = state.DisplayName!,
            PasswordHash = state.PasswordHash,
        };
        Check(await users.CreateAsync(admin));
        Check(await users.SetAuthenticatorAsync(admin, key, step));
        Check(await users.AddToRoleAsync(admin, Roles.Admin));
        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(admin, RecoveryCodeCount) ?? throw new InvalidOperationException("No recovery codes.");
        await users.MarkSetupCompleteAsync(admin);
        await audit.WriteAsync(AuditActions.SetupCompleted, AuditActor.User(admin.Id), "User", admin.Id, new { userName = admin.UserName });
        return (admin, [.. codes]);
    }

    private static async Task<(GoldmarkUser, IReadOnlyList<string>)> CompleteLinkAsync(EnrolmentState state, GoldmarkUser user, string key,
        long step, GoldmarkUserManager users, LinkStore links, AuditTrail audit)
    {
        if (!await links.RedeemAsync(state.LinkId!.Value))
        {
            throw new LinkClosedException();
        }

        if (state.Kind == EnrolmentKind.Invitation)
        {
            Check(await users.SetPasswordHashAsync(user, state.PasswordHash!));
        }

        Check(await users.SetAuthenticatorAsync(user, key, step));
        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount) ?? throw new InvalidOperationException("No recovery codes.");
        await audit.WriteAsync(state.Kind == EnrolmentKind.Invitation ? AuditActions.InvitationRedeemed : AuditActions.TwoFactorResetCompleted,
            AuditActor.User(user.Id), "User", user.Id);
        return (user, [.. codes]);
    }

    /// <summary>
    /// The enrolment in this request's cookie, if it still applies: the setup token is still this
    /// process's (setup), or the link is still open and the person unchanged since it was confirmed.
    /// </summary>
    private static Task<(EnrolmentState State, GoldmarkUser? User)?> CurrentAsync(HttpContext context, GoldmarkUserManager users,
        SetupState setup, LinkStore links) => ResolveAsync(context.User, users, setup, links);

    /// <summary>The enrolment an enrolment-cookie principal describes, if it still applies (see <see cref="CurrentAsync"/>).</summary>
    internal static async Task<(EnrolmentState State, GoldmarkUser? User)?> ResolveAsync(System.Security.Claims.ClaimsPrincipal principal,
        GoldmarkUserManager users, SetupState setup, LinkStore links)
    {
        if (EnrolmentState.From(principal) is not { } state)
        {
            return null;
        }

        if (state.Kind == EnrolmentKind.Setup)
        {
            return state.SetupTokenHash is { } hash && setup.IsCurrent(Convert.FromBase64String(hash)) ? (state, null) : null;
        }

        var user = state.UserId is { } id ? await users.FindByIdAsync(id.ToString("D")) : null;
        if (user is null || user.IsDisabled || user.SecurityStamp != state.SecurityStamp)
        {
            return null;
        }

        if (state.Kind == EnrolmentKind.ChangeAuthenticator)
        {
            return user.HasAuthenticator ? (state, user) : null;
        }

        var link = state.LinkId is { } linkId ? await links.FindAsync(linkId) : null;
        return link is { IsOpen: true } && !links.IsExpired(link) && link.UserId == user.Id ? (state, user) : null;
    }

    private static async Task<IResult> EnrolmentExpiredAsync(HttpContext context)
    {
        await EnrolmentEndAsync(context);
        return ApiProblems.Problem(StatusCodes.Status401Unauthorized, "enrolment-expired", "This step has expired. Start again.");
    }

    private static Task EnrolmentEndAsync(HttpContext context) => EnrolmentState.EndAsync(context);

    private static IResult LinkInvalid() =>
        ApiProblems.Problem(StatusCodes.Status410Gone, "link-invalid", "This link is no longer valid. Ask your administrator for a new one.");

    private static IResult SetupComplete() =>
        ApiProblems.Problem(StatusCodes.Status409Conflict, "setup-complete", "Setup is already complete. Sign in.");

    private static string Purpose(LinkRow link) => link.Purpose == LinkPurpose.Invitation ? "invitation" : "two-factor-reset";

    private static string KindName(EnrolmentKind kind) => kind switch
    {
        EnrolmentKind.Setup => "setup",
        EnrolmentKind.Invitation => "invitation",
        EnrolmentKind.TwoFactorReset => "two-factor-reset",
        _ => "new-authenticator",
    };

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Identity refused: {string.Join(", ", result.Errors.Select(e => e.Code))}.");
        }
    }

    private sealed class LinkClosedException : Exception;
}

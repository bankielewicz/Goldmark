using System.Data;
using Goldmark.Api.Audit;
using Goldmark.Api.Auth;
using Goldmark.Api.Data;
using Goldmark.Api.Hosting;
using Goldmark.Api.Security;
using Microsoft.AspNetCore.Identity;

namespace Goldmark.Api.Users;

/// <summary>
/// Users &amp; roles (screen 17; US-04, US-03; DEC-038 items 2, 8 and 10; DEC-042), Admin only. Every
/// change is audited, in the same transaction. Guards hold here whatever the UI shows: nobody locks,
/// disables, resets or removes Admin from themselves; the last enabled Admin keeps Admin and stays
/// enabled; Admin is never granted by an invitation, only to someone who has signed in; Collector is
/// never a person's role. Changing roles needs the user's ETag (If-Match): HTTP 412 if someone else
/// changed them. Dual control (DEC-042): an invitation, or a password-and-two-factor reset, asked for
/// while another Admin could approve it gives the one who asks the link only; another Admin approves
/// and gets the code. No link alone ever yields an Admin session: the password-and-two-factor reset is
/// never for an Admin, and Admin is not added while one is open.
/// </summary>
internal static class UserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder api)
    {
        var users = api.MapGroup("/users");

        api.MapGet("/users", async (UserDirectory directory) => Results.Ok(new { users = await directory.ListAsync() }))
            .RequireAccess(AccessPolicy.Admin)
            .WithDisplayName("Users: list");

        users.MapGet("/export", async (UserDirectory directory, TimeProvider time) =>
                Results.File(Csv.Utf8WithBom(UserDirectory.ToCsv(await directory.ListAsync())), "text/csv; charset=utf-8",
                    $"goldmark-users-{time.GetUtcNow():yyyyMMdd}.csv"))
            .RequireAccess(AccessPolicy.Admin)
            .WithDisplayName("Users: export CSV");

        users.MapGet("/{id:guid}", GetAsync)
            .RequireAccess(AccessPolicy.Admin)
            .WithDisplayName("Users: get");

        api.MapPost("/users", CreateAsync)
            .RequireAccess(AccessPolicy.Admin)
            .WithDisplayName("Users: invite");

        users.MapPut("/{id:guid}/roles", SetRolesAsync)
            .RequireAccess(AccessPolicy.Admin)
            .WithDisplayName("Users: set roles");

        users.MapPost("/{id:guid}/lock", (Guid id, HttpContext context, IServiceProvider services) => ChangeAsync(id, context, services, Change.Lock))
            .RequireAccess(AccessPolicy.Admin)
            .WithDisplayName("Users: lock");

        users.MapPost("/{id:guid}/unlock", (Guid id, HttpContext context, IServiceProvider services) => ChangeAsync(id, context, services, Change.Unlock))
            .RequireAccess(AccessPolicy.Admin)
            .WithDisplayName("Users: unlock");

        users.MapPost("/{id:guid}/disable", (Guid id, HttpContext context, IServiceProvider services) => ChangeAsync(id, context, services, Change.Disable))
            .RequireAccess(AccessPolicy.Admin)
            .WithDisplayName("Users: disable");

        users.MapPost("/{id:guid}/enable", (Guid id, HttpContext context, IServiceProvider services) => ChangeAsync(id, context, services, Change.Enable))
            .RequireAccess(AccessPolicy.Admin)
            .WithDisplayName("Users: enable");

        users.MapPost("/{id:guid}/two-factor-reset", ResetTwoFactorAsync)
            .RequireAccess(AccessPolicy.Admin)
            .WithDisplayName("Users: reset two-factor");

        users.MapPost("/{id:guid}/invitation", ReissueInvitationAsync)
            .RequireAccess(AccessPolicy.Admin)
            .WithDisplayName("Users: new invitation link");

        users.MapDelete("/{id:guid}/invitation", RevokeInvitationAsync)
            .RequireAccess(AccessPolicy.Admin)
            .WithDisplayName("Users: revoke invitation");

        users.MapPost("/{id:guid}/password-reset", ResetPasswordAsync)
            .RequireAccess(AccessPolicy.Admin)
            .WithDisplayName("Users: reset password");

        users.MapPost("/{id:guid}/request/approve", ApproveRequestAsync)
            .RequireAccess(AccessPolicy.Admin)
            .WithDisplayName("Users: approve a request");

        users.MapPost("/{id:guid}/request/decline", (Guid id, HttpContext context, IServiceProvider services) =>
                CloseRequestAsync(id, context, services, LinkClosure.Declined))
            .RequireAccess(AccessPolicy.Admin)
            .WithDisplayName("Users: decline a request");

        users.MapPost("/{id:guid}/request/cancel", (Guid id, HttpContext context, IServiceProvider services) =>
                CloseRequestAsync(id, context, services, LinkClosure.Cancelled))
            .RequireAccess(AccessPolicy.Admin)
            .WithDisplayName("Users: cancel a request");

        users.MapGet("/approvals", async (HttpContext context, UserDirectory directory) =>
                Results.Ok(new { waiting = await directory.CountApprovalsWaitingForAsync(context.User.UserId()!.Value) }))
            .RequireAccess(AccessPolicy.Admin)
            .WithDisplayName("Users: requests waiting for me");

        return users;
    }

    internal sealed record CreateRequest(string? DisplayName, string? UserName, IReadOnlyList<string>? Roles);

    internal sealed record RolesRequest(IReadOnlyList<string>? Roles);

    internal sealed record PasswordResetRequest(bool? AlsoTwoFactor);

    /// <summary>
    /// What the Admin who asked sees, once (DEC-042): the link, and its code unless another Admin must
    /// approve it (<c>AwaitingApproval</c>); <c>OnlyAdmin</c> when dual control would apply but nobody else
    /// could approve, so the one who asked gets both.
    /// </summary>
    internal sealed record LinkShownOnce(string Token, string? Code, DateTime ExpiresAt, int ExpiryHours, int CodeAttempts,
        bool AwaitingApproval, bool OnlyAdmin)
    {
        public static LinkShownOnce Of(IssuedLink link, bool onlyAdmin = false) =>
            new(link.Token, link.Code, link.ExpiresAt, link.ExpiryHours, link.CodeAttempts, false, onlyAdmin);

        public static LinkShownOnce Of(RequestedLink link) =>
            new(link.Token, null, link.ExpiresAt, link.ExpiryHours, link.CodeAttempts, true, false);
    }

    private enum Change
    {
        Lock,
        Unlock,
        Disable,
        Enable,
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext context, UserDirectory directory)
    {
        if (await directory.FindAsync(id) is not { } user)
        {
            return NotFound();
        }

        context.Response.Headers.ETag = Quote(user.ETag);
        return Results.Ok(user);
    }

    /// <summary>
    /// Creates the account (no password yet) and its invitation: a link and a separate code, shown once;
    /// under dual control, the link only, and another Admin approves and gets the code (DEC-042).
    /// </summary>
    private static async Task<IResult> CreateAsync(CreateRequest request, HttpContext context, GoldmarkUserManager users,
        LinkStore links, UserDirectory directory, DbSession db, AuditTrail audit)
    {
        var roles = (request.Roles ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (roles.Count == 0)
        {
            return ApiProblems.Field("roles", "RolesRequired");
        }

        if (roles.Any(r => !Roles.ForInvitations.Contains(Roles.PersonRole(r) ?? "", StringComparer.Ordinal)))
        {
            // Admin is added after the first sign-in; Collector never (DEC-038 items 2 and 10).
            return ApiProblems.Field("roles", "RoleNotAllowedForInvitation");
        }

        var admin = context.User.UserId()!.Value;
        var user = new GoldmarkUser
        {
            Id = Guid.CreateVersion7(),
            UserName = request.UserName?.Trim() ?? "",
            DisplayName = request.DisplayName?.Trim() ?? "",
            CreatedBy = admin,
        };
        var result = await db.InTransactionAsync(async () =>
        {
            var approvers = await directory.ApproversAsync(admin, user.Id);
            if (NoApproverCanSignIn(approvers) is { } refused)
            {
                return (Problem: refused, Link: (LinkShownOnce?)null);
            }

            var created = await users.CreateAsync(user);
            if (!created.Succeeded)
            {
                return (Problem: ApiProblems.Validation(created), Link: (LinkShownOnce?)null);
            }

            foreach (var role in roles)
            {
                Check(await users.AddToRoleAsync(user, Roles.PersonRole(role)!));
            }

            var link = await InvitationLinkAsync(user, admin, links, approvers.Count > 0);
            await audit.WriteAsync(AuditActions.InvitationCreated, "User", user.Id, new
            {
                userName = user.UserName,
                roles = roles.Select(r => Roles.PersonRole(r)),
                expiresAt = link.ExpiresAt,
                approval = link.AwaitingApproval ? "requested" : "only-admin",
            });
            return (Problem: (IResult?)null, Link: link);
        });
        if (result.Problem is not null)
        {
            return result.Problem;
        }

        return Results.Created($"/api/v1/users/{user.Id:D}", new { user = await directory.FindAsync(user.Id), link = result.Link });
    }

    private static async Task<IResult> SetRolesAsync(Guid id, RolesRequest request, HttpContext context, GoldmarkUserManager users,
        LinkStore links, UserDirectory directory, DbSession db, AuditTrail audit)
    {
        if (IfMatch(context) is not { } etag)
        {
            return ApiProblems.Problem(StatusCodes.Status428PreconditionRequired, "if-match-required", "Send the user's ETag in If-Match.");
        }

        var wanted = new List<string>();
        foreach (var name in (request.Roles ?? []).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Roles.PersonRole(name) is not { } role)
            {
                return ApiProblems.Field("roles", "RoleNotAllowed");
            }

            wanted.Add(role);
        }

        if (wanted.Count == 0)
        {
            return ApiProblems.Field("roles", "RolesRequired");
        }

        var self = context.User.UserId()!.Value;
        var outcome = await ConcurrentlyAsync(() => db.InTransactionAsync(async () =>
        {
            var user = await users.FindByIdAsync(id.ToString("D"));
            if (user is null)
            {
                return NotFound();
            }

            if (user.ConcurrencyStamp != etag)
            {
                return Conflict();
            }

            var before = await users.GetRolesAsync(user);
            var add = wanted.Except(before, StringComparer.Ordinal).ToList();
            var remove = before.Except(wanted, StringComparer.Ordinal).ToList();
            if (add.Contains(Roles.Admin) && (user.IsPending || user.LastSignInAt is null))
            {
                return ApiProblems.Problem(StatusCodes.Status409Conflict, "admin-needs-sign-in",
                    "Admin can be added only after the person has signed in once.");
            }

            if (add.Contains(Roles.Admin) && user.IsPasswordResetPending)
            {
                // Its link may be one that needs no factor of theirs, and an Admin is never reset that way (DEC-042 item 7).
                return ApiProblems.Problem(StatusCodes.Status409Conflict, "password-reset-pending",
                    "Admin can't be added while this person's password reset is unfinished. Add it after they set their new password.");
            }

            if (add.Contains(Roles.Admin) && await links.OpenForUserAsync(user.Id) is { Purpose: LinkPurpose.CombinedReset } combined
                && !links.IsExpired(combined))
            {
                // That link sets a new password and authenticator from the link and code alone (DEC-042).
                return ApiProblems.Problem(StatusCodes.Status409Conflict, "combined-reset-open",
                    "Admin can't be added while a password and two-factor reset is open for this person.");
            }

            if (remove.Contains(Roles.Admin))
            {
                if (user.Id == self)
                {
                    return ApiProblems.Problem(StatusCodes.Status409Conflict, "own-admin", "You can't remove Admin from yourself.");
                }

                if (!user.IsDisabled && await directory.CountEnabledAdministratorsAsync() <= 1)
                {
                    return LastAdmin();
                }
            }

            foreach (var role in add)
            {
                Check(await users.AddToRoleAsync(user, role));
            }

            // Identity's role changes also update the account row, so its ETag changes too.
            foreach (var role in remove)
            {
                Check(await users.RemoveFromRoleAsync(user, role));
            }

            await audit.WriteAsync(AuditActions.RolesChanged, "User", user.Id, new { userName = user.UserName, before, after = wanted });
            return null;
        }, IsolationLevel.Serializable));

        return outcome ?? await GetAsync(id, context, directory);
    }

    private static async Task<IResult> ChangeAsync(Guid id, HttpContext context, IServiceProvider services, Change change)
    {
        var users = services.GetRequiredService<GoldmarkUserManager>();
        var directory = services.GetRequiredService<UserDirectory>();
        var db = services.GetRequiredService<DbSession>();
        var audit = services.GetRequiredService<AuditTrail>();
        var self = context.User.UserId()!.Value;

        var outcome = await ConcurrentlyAsync(() => db.InTransactionAsync(async () =>
        {
            var user = await users.FindByIdAsync(id.ToString("D"));
            if (user is null)
            {
                return NotFound();
            }

            if (user.IsPending)
            {
                return ApiProblems.Problem(StatusCodes.Status409Conflict, "invitation-pending", "This person has not accepted the invitation yet.");
            }

            if (user.Id == self && change is Change.Lock or Change.Disable)
            {
                return ApiProblems.Problem(StatusCodes.Status409Conflict, "self", change == Change.Lock
                    ? "You can't lock your own account." : "You can't disable your own account.");
            }

            string action;
            switch (change)
            {
                case Change.Lock:
                    Check(await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue));
                    await SessionTicketStore.EndAllAsync(db, user.Id);
                    action = AuditActions.Locked;
                    break;
                case Change.Unlock:
                    Check(await users.SetLockoutEndDateAsync(user, null));
                    Check(await users.ResetAccessFailedCountAsync(user));
                    action = AuditActions.Unlocked;
                    break;
                case Change.Disable:
                    if (await users.IsInRoleAsync(user, Roles.Admin) && !user.IsDisabled && await directory.CountEnabledAdministratorsAsync() <= 1)
                    {
                        return LastAdmin();
                    }

                    user.IsDisabled = true;
                    Check(await users.UpdateAsync(user));
                    await SessionTicketStore.EndAllAsync(db, user.Id);
                    action = AuditActions.Disabled;
                    break;
                default:
                    user.IsDisabled = false;
                    Check(await users.UpdateAsync(user));
                    action = AuditActions.Enabled;
                    break;
            }

            await audit.WriteAsync(action, "User", user.Id, new { userName = user.UserName });
            return null;
        }, IsolationLevel.Serializable));

        return outcome ?? await GetAsync(id, context, directory);
    }

    /// <summary>
    /// Clears the authenticator and recovery codes, ends every session and issues a link and code to set
    /// up two-factor again; the password is kept (DEC-038 item 8).
    /// </summary>
    private static async Task<IResult> ResetTwoFactorAsync(Guid id, HttpContext context, GoldmarkUserManager users, LinkStore links,
        UserDirectory directory, DbSession db, AuditTrail audit)
    {
        var self = context.User.UserId()!.Value;
        if (id == self)
        {
            return ApiProblems.Problem(StatusCodes.Status409Conflict, "self", "You can't reset your own two-factor here.");
        }

        return await db.InTransactionAsync(async () =>
        {
            var user = await users.FindByIdAsync(id.ToString("D"));
            if (user is null)
            {
                return NotFound();
            }

            if (user.IsPending)
            {
                return ApiProblems.Problem(StatusCodes.Status409Conflict, "invitation-pending", "This person has not accepted the invitation yet.");
            }

            if (user.IsPasswordResetPending)
            {
                // The two-factor reset's link asks for the current password, which a password reset refuses.
                return ApiProblems.Problem(StatusCodes.Status409Conflict, "password-reset-pending",
                    "This person's password was reset. Use Reset password with \"Also reset two-factor\" instead.");
            }

            if (await WaitingRequestAsync(user, links) is not null)
            {
                return RequestWaiting();
            }

            Check(await users.ClearTwoFactorAsync(user));
            await SessionTicketStore.EndAllAsync(db, user.Id);
            var link = await links.IssueAsync(user.Id, LinkPurpose.TwoFactorReset, self);
            await audit.WriteAsync(AuditActions.TwoFactorReset, "User", user.Id, new { userName = user.UserName, expiresAt = link.ExpiresAt });
            return Results.Ok(new { user = await directory.FindAsync(user.Id), link });
        });
    }

    /// <summary>"Copy new link": a new invitation link and code (or, under dual control, a new request); the old ones stop working.</summary>
    private static async Task<IResult> ReissueInvitationAsync(Guid id, HttpContext context, GoldmarkUserManager users, LinkStore links,
        UserDirectory directory, DbSession db, AuditTrail audit)
    {
        var self = context.User.UserId()!.Value;
        return await db.InTransactionAsync(async () =>
        {
            var user = await users.FindByIdAsync(id.ToString("D"));
            if (user is not { IsPending: true })
            {
                return user is null ? NotFound() : NoInvitation();
            }

            // A waiting request is approved, declined or cancelled first, as for the resets.
            if (await WaitingRequestAsync(user, links) is not null)
            {
                return RequestWaiting();
            }

            var approvers = await directory.ApproversAsync(self, user.Id);
            if (NoApproverCanSignIn(approvers) is { } refused)
            {
                return refused;
            }

            var link = await InvitationLinkAsync(user, self, links, approvers.Count > 0);
            await audit.WriteAsync(AuditActions.InvitationReissued, "User", user.Id, new
            {
                userName = user.UserName,
                expiresAt = link.ExpiresAt,
                approval = link.AwaitingApproval ? "requested" : "only-admin",
            });
            return Results.Ok(new { user = await directory.FindAsync(user.Id), link });
        });
    }

    /// <summary>
    /// Revokes an unused invitation and removes the account it created (owner, DEC-040): the user name
    /// is free again; the audit log keeps the invitation and its revocation.
    /// </summary>
    private static async Task<IResult> RevokeInvitationAsync(Guid id, GoldmarkUserManager users, LinkStore links, DbSession db, AuditTrail audit) =>
        await db.InTransactionAsync(async () =>
        {
            var user = await users.FindByIdAsync(id.ToString("D"));
            if (user is not { IsPending: true })
            {
                return user is null ? NotFound() : NoInvitation();
            }

            await links.RevokeOpenAsync(user.Id);
            Check(await users.DeleteAsync(user));
            await audit.WriteAsync(AuditActions.InvitationRevoked, "User", user.Id, new { userName = user.UserName });
            return Results.NoContent();
        });

    /// <summary>
    /// An invitation's link: a request (link only) when another Admin could approve it, else the link and
    /// its code, marked as given to the only Admin (DEC-042).
    /// </summary>
    private static async Task<LinkShownOnce> InvitationLinkAsync(GoldmarkUser user, Guid asker, LinkStore links, bool dualControl) =>
        dualControl
            ? LinkShownOnce.Of(await links.RequestAsync(user.Id, LinkPurpose.Invitation, asker))
            : LinkShownOnce.Of(await links.IssueAsync(user.Id, LinkPurpose.Invitation, asker), onlyAdmin: true);

    /// <summary>
    /// Under dual control (DEC-042 item 8): null when no other Admin exists (the one who asks gets both
    /// values) or one of them can sign in to approve; otherwise the refusal, naming who can't and why
    /// (owner, 2026-10-22: "C. Refuse, say why"), so no request waits for an approval that can't come.
    /// </summary>
    private static IResult? NoApproverCanSignIn(IReadOnlyList<Approver> approvers) =>
        approvers.Count == 0 || approvers.Any(a => a.CanApprove)
            ? null
            : ApiProblems.Problem(StatusCodes.Status409Conflict, "no-approver-available",
                $"No other Admin can approve now: {string.Join("; ", approvers.Select(Why))}. Unlock them, or let them finish their reset, first.");

    private static string Why(Approver approver) => approver switch
    {
        { LockedByAdministrator: true } => $"{approver.DisplayName} is locked by an administrator",
        { PasswordResetPending: true } => $"{approver.DisplayName}'s password reset isn't finished",
        _ => $"{approver.DisplayName} has no authenticator yet",
    };

    /// <summary>
    /// "Reset password…" (DEC-042). Password only: the old password stops working now, every session
    /// ends, and a link and code let the person choose a new one, proved with their own authenticator.
    /// With two-factor too (never for an Admin): under dual control nothing changes until another Admin
    /// approves; with no other Admin, the password and authenticator are cleared now and the one who
    /// asked gets the link and code.
    /// </summary>
    private static async Task<IResult> ResetPasswordAsync(Guid id, PasswordResetRequest request, HttpContext context,
        GoldmarkUserManager users, LinkStore links, UserDirectory directory, DbSession db, AuditTrail audit, TimeProvider time)
    {
        var self = context.User.UserId()!.Value;
        if (id == self)
        {
            return ApiProblems.Problem(StatusCodes.Status409Conflict, "self",
                "You can't reset your own password here. Use Change password in your account menu.");
        }

        var alsoTwoFactor = request.AlsoTwoFactor ?? false;
        var outcome = await ConcurrentlyAsync(() => db.InTransactionAsync<IResult?>(async () =>
        {
            var user = await users.FindByIdAsync(id.ToString("D"));
            if (user is null)
            {
                return NotFound();
            }

            if (user.IsPending)
            {
                return ApiProblems.Problem(StatusCodes.Status409Conflict, "invitation-pending", "This person has not accepted the invitation yet.");
            }

            if (user.IsDisabled)
            {
                return ApiProblems.Problem(StatusCodes.Status409Conflict, "disabled", "Enable the account first. A disabled account can't use the link.");
            }

            var isAdmin = await users.IsInRoleAsync(user, Roles.Admin);
            if (alsoTwoFactor && isAdmin)
            {
                return AdminTarget();
            }

            if (!alsoTwoFactor && !user.HasAuthenticator)
            {
                // The password-only reset ends with the person's own authenticator (DEC-042 item 3): without one its
                // link could never be used.
                return ApiProblems.Problem(StatusCodes.Status409Conflict, "no-authenticator", isAdmin
                    ? "This Admin has no authenticator to confirm a new password with, and the password-and-two-factor reset isn't for an Admin. Remove Admin first, or let them finish setting up two-factor."
                    : "This person has no authenticator to confirm a new password with. Use \"Also reset two-factor\" instead.");
            }

            // A request that ended unapproved no longer counts: asking again replaces it.
            if (await WaitingRequestAsync(user, links) is not null)
            {
                return RequestWaiting();
            }

            var approvers = alsoTwoFactor ? await directory.ApproversAsync(self, user.Id) : [];
            if (NoApproverCanSignIn(approvers) is { } refused)
            {
                return refused;
            }

            LinkShownOnce link;
            if (alsoTwoFactor && approvers.Count > 0)
            {
                link = LinkShownOnce.Of(await links.RequestAsync(user.Id, LinkPurpose.CombinedReset, self));
                await audit.WriteAsync(AuditActions.ApprovalRequested, "User", user.Id,
                    new { userName = user.UserName, @for = "combined-reset", expiresAt = link.ExpiresAt });
            }
            else
            {
                await StartResetAsync(user, alsoTwoFactor, users, db, time);
                var issued = await links.IssueAsync(user.Id, alsoTwoFactor ? LinkPurpose.CombinedReset : LinkPurpose.PasswordReset, self);
                link = LinkShownOnce.Of(issued, onlyAdmin: alsoTwoFactor);
                await audit.WriteAsync(AuditActions.PasswordReset, "User", user.Id, alsoTwoFactor
                    ? new { userName = user.UserName, twoFactorToo = true, expiresAt = link.ExpiresAt, approval = (string?)"only-admin" }
                    : new { userName = user.UserName, twoFactorToo = false, expiresAt = link.ExpiresAt, approval = (string?)null });
            }

            return Results.Ok(new { user = await directory.FindAsync(user.Id), link });
        }, IsolationLevel.Serializable));
        return outcome!;
    }

    /// <summary>
    /// What a password reset changes on the account, at once or, under dual control, at approval
    /// (DEC-042): the old password stops working, a failed-attempt lockout ends, with two-factor too the
    /// authenticator and recovery codes go, and every session ends.
    /// </summary>
    private static async Task StartResetAsync(GoldmarkUser user, bool alsoTwoFactor, GoldmarkUserManager users, DbSession db, TimeProvider time)
    {
        Check(await users.StartPasswordResetAsync(user, time.GetUtcNow().UtcDateTime));
        if (alsoTwoFactor)
        {
            Check(await users.ClearTwoFactorAsync(user));
        }

        await SessionTicketStore.EndAllAsync(db, user.Id);
    }

    /// <summary>
    /// Another Admin approves a waiting request (DEC-042): the code is made now and shown once to them;
    /// a password-and-two-factor reset takes effect now. Not one's own request, nor one about oneself.
    /// </summary>
    private static async Task<IResult> ApproveRequestAsync(Guid id, HttpContext context, GoldmarkUserManager users, LinkStore links,
        UserDirectory directory, DbSession db, AuditTrail audit, TimeProvider time)
    {
        var self = context.User.UserId()!.Value;
        if (id == self)
        {
            return ApiProblems.Problem(StatusCodes.Status409Conflict, "self", "You can't approve a request about yourself.");
        }

        var outcome = await ConcurrentlyAsync(() => db.InTransactionAsync<IResult?>(async () =>
        {
            var user = await users.FindByIdAsync(id.ToString("D"));
            if (user is null)
            {
                return NotFound();
            }

            if (await WaitingRequestAsync(user, links) is not { } request)
            {
                return NoRequest(await links.OpenForUserAsync(user.Id));
            }

            if (request.CreatedBy == self)
            {
                return ApiProblems.Problem(StatusCodes.Status409Conflict, "own-request", "Another Admin approves requests you make.");
            }

            if (user.IsDisabled)
            {
                return ApiProblems.Problem(StatusCodes.Status409Conflict, "disabled", "Enable the account first. A disabled account can't use the link.");
            }

            var combined = request.Purpose == LinkPurpose.CombinedReset;
            if (combined && await users.IsInRoleAsync(user, Roles.Admin))
            {
                return AdminTarget();
            }

            // The guarded approval first: if the request ended meanwhile, nothing on the account changes.
            if (await links.ApproveAsync(request.Id, self) is not { } code)
            {
                return NoRequest(null);
            }

            if (combined)
            {
                await StartResetAsync(user, alsoTwoFactor: true, users, db, time);
            }

            await audit.WriteAsync(AuditActions.RequestApproved, "User", user.Id, new
            {
                userName = user.UserName,
                @for = combined ? "combined-reset" : "invitation",
                askedBy = await users.AuditNameAsync(request.CreatedBy),
                linkExpiresAt = code.ExpiresAt,
            });
            return Results.Ok(new { user = await directory.FindAsync(user.Id), code });
        }, IsolationLevel.Serializable));
        return outcome!;
    }

    /// <summary>
    /// Declines (another Admin) or cancels (the one who asked) a waiting request (DEC-042). An invitation's
    /// account goes with it, as when an unused invitation is revoked (DEC-040); a reset's account was not
    /// changed, so only the link closes.
    /// </summary>
    private static async Task<IResult> CloseRequestAsync(Guid id, HttpContext context, IServiceProvider services, string reason)
    {
        var users = services.GetRequiredService<GoldmarkUserManager>();
        var links = services.GetRequiredService<LinkStore>();
        var db = services.GetRequiredService<DbSession>();
        var audit = services.GetRequiredService<AuditTrail>();
        var self = context.User.UserId()!.Value;
        var outcome = await ConcurrentlyAsync(() => db.InTransactionAsync<IResult?>(async () =>
        {
            var user = await users.FindByIdAsync(id.ToString("D"));
            if (user is null)
            {
                return NotFound();
            }

            if (await WaitingRequestAsync(user, links, includeEnded: true) is not { } request)
            {
                return NoRequest(null);
            }

            var own = request.CreatedBy == self;
            if (reason == LinkClosure.Declined && own)
            {
                return ApiProblems.Problem(StatusCodes.Status409Conflict, "own-request", "Cancel your own request instead.");
            }

            if (reason == LinkClosure.Cancelled && !own)
            {
                return ApiProblems.Problem(StatusCodes.Status409Conflict, "not-own-request",
                    "Only the Admin who asked can cancel it. Decline it instead.");
            }

            if (!await links.CloseRequestAsync(request.Id, self, reason))
            {
                return NoRequest(null);
            }

            var invitation = request.Purpose == LinkPurpose.Invitation;
            if (invitation)
            {
                Check(await users.DeleteAsync(user));
            }

            await audit.WriteAsync(reason == LinkClosure.Declined ? AuditActions.RequestDeclined : AuditActions.RequestCancelled, "User", user.Id, new
            {
                userName = user.UserName,
                @for = invitation ? "invitation" : "combined-reset",
                askedBy = await users.AuditNameAsync(request.CreatedBy),
                accountRemoved = invitation,
            });
            return Results.NoContent();
        }, IsolationLevel.Serializable));
        return outcome!;
    }

    /// <summary>The person's request awaiting approval, if any; one that ended unapproved only with <paramref name="includeEnded"/>.</summary>
    private static async Task<LinkRow?> WaitingRequestAsync(GoldmarkUser user, LinkStore links, bool includeEnded = false) =>
        await links.OpenForUserAsync(user.Id) is { IsAwaitingApproval: true } link && (includeEnded || !links.IsExpired(link)) ? link : null;

    private static IResult NoRequest(LinkRow? link) =>
        link is { IsAwaitingApproval: true }
            ? ApiProblems.Problem(StatusCodes.Status409Conflict, "request-ended",
                "This request ended without approval. The Admin who asked can ask again.")
            : ApiProblems.Problem(StatusCodes.Status409Conflict, "no-request", "There's no request waiting for this person.");

    private static IResult RequestWaiting() =>
        ApiProblems.Problem(StatusCodes.Status409Conflict, "request-waiting",
            "A request for this person is waiting for another Admin's approval. Approve, decline or cancel it first.");

    private static IResult AdminTarget() =>
        ApiProblems.Problem(StatusCodes.Status409Conflict, "admin-target", "Remove Admin first; add it again after their first sign-in.");

    /// <summary>A concurrent change to the same account (the transaction rolled back) answers 412.</summary>
    private static async Task<IResult?> ConcurrentlyAsync(Func<Task<IResult?>> work)
    {
        try
        {
            return await work();
        }
        catch (DBConcurrencyException)
        {
            return Conflict();
        }
    }

    private static string? IfMatch(HttpContext context)
    {
        var value = context.Request.Headers.IfMatch.ToString().Trim();
        if (value.StartsWith("W/", StringComparison.Ordinal))
        {
            value = value[2..];
        }

        return value.Length > 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : null;
    }

    private static string Quote(string etag) => $"\"{etag}\"";

    private static IResult NotFound() => ApiProblems.Problem(StatusCodes.Status404NotFound, "not-found", "No such user.");

    private static IResult Conflict() =>
        ApiProblems.Problem(StatusCodes.Status412PreconditionFailed, "changed", "Someone else changed this person meanwhile. Reload and try again.");

    private static IResult LastAdmin() =>
        ApiProblems.Problem(StatusCodes.Status409Conflict, "last-admin", "Goldmark needs at least one enabled Admin.");

    private static IResult NoInvitation() =>
        ApiProblems.Problem(StatusCodes.Status409Conflict, "no-invitation", "This person has already accepted the invitation.");

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            // A concurrent change to the same account; the transaction rolls back.
            throw new DBConcurrencyException(string.Join(", ", result.Errors.Select(e => e.Code)));
        }
    }
}

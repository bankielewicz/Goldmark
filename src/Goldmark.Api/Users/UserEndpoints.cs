using System.Data;
using Goldmark.Api.Audit;
using Goldmark.Api.Auth;
using Goldmark.Api.Data;
using Goldmark.Api.Hosting;
using Goldmark.Api.Security;
using Microsoft.AspNetCore.Identity;

namespace Goldmark.Api.Users;

/// <summary>
/// Users &amp; roles (screen 17; US-04, US-03; DEC-038 items 2, 8 and 10), Admin only. Every change is
/// audited, in the same transaction. Guards hold here whatever the UI shows: nobody locks, disables,
/// resets or removes Admin from themselves; the last enabled Admin keeps Admin and stays enabled; Admin
/// is never granted by an invitation, only to someone who has signed in; Collector is never a
/// person's role. Changing roles needs the user's ETag (If-Match): HTTP 412 if someone else changed them.
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

        return users;
    }

    internal sealed record CreateRequest(string? DisplayName, string? UserName, IReadOnlyList<string>? Roles);

    internal sealed record RolesRequest(IReadOnlyList<string>? Roles);

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

    /// <summary>Creates the account (no password yet) and its invitation: a link and a separate code, shown once.</summary>
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
            var created = await users.CreateAsync(user);
            if (!created.Succeeded)
            {
                return (Problem: ApiProblems.Validation(created), Link: (IssuedLink?)null);
            }

            foreach (var role in roles)
            {
                Check(await users.AddToRoleAsync(user, Roles.PersonRole(role)!));
            }

            var link = await links.IssueAsync(user.Id, LinkPurpose.Invitation, admin);
            await audit.WriteAsync(AuditActions.InvitationCreated, "User", user.Id,
                new { userName = user.UserName, roles = roles.Select(r => Roles.PersonRole(r)), expiresAt = link.ExpiresAt });
            return (Problem: (IResult?)null, Link: link);
        });
        if (result.Problem is not null)
        {
            return result.Problem;
        }

        return Results.Created($"/api/v1/users/{user.Id:D}", new { user = await directory.FindAsync(user.Id), link = result.Link });
    }

    private static async Task<IResult> SetRolesAsync(Guid id, RolesRequest request, HttpContext context, GoldmarkUserManager users,
        UserDirectory directory, DbSession db, AuditTrail audit)
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

            Check(await users.ClearTwoFactorAsync(user));
            await SessionTicketStore.EndAllAsync(db, user.Id);
            var link = await links.IssueAsync(user.Id, LinkPurpose.TwoFactorReset, self);
            await audit.WriteAsync(AuditActions.TwoFactorReset, "User", user.Id, new { userName = user.UserName, expiresAt = link.ExpiresAt });
            return Results.Ok(new { user = await directory.FindAsync(user.Id), link });
        });
    }

    /// <summary>"Copy new link": a new invitation link and code; the old ones stop working.</summary>
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

            var link = await links.IssueAsync(user.Id, LinkPurpose.Invitation, self);
            await audit.WriteAsync(AuditActions.InvitationReissued, "User", user.Id, new { userName = user.UserName, expiresAt = link.ExpiresAt });
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

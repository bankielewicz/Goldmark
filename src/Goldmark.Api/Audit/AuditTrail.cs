using System.Text.Json;
using Goldmark.Api.Auth;
using Goldmark.Api.Data;

namespace Goldmark.Api.Audit;

/// <summary>
/// Appends rows to <c>cmdb.AuditLog</c> (US-07; DEC-038 item 1), inside the caller's transaction, so
/// a change and its audit row commit or roll back together. The runtime login can only insert and
/// read there (0002). Slice 4 adds the screen, filters, export and retention. Rows never hold a
/// secret: no password, code, token or key, and no user name someone typed for an account that does
/// not exist (it may be a password typed into the wrong field).
/// </summary>
internal sealed class AuditTrail(DbSession db, IHttpContextAccessor http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Records <paramref name="action"/> by the signed-in person, or by an anonymous client.</summary>
    public Task WriteAsync(string action, string? targetType = null, Guid? targetId = null, object? details = null)
    {
        var actorId = http.HttpContext?.User.Identity?.AuthenticationType == AuthSchemes.Session ? http.HttpContext.User.UserId() : null;
        return actorId is { } id
            ? WriteAsync(action, AuditActor.User(id), targetType, targetId, details)
            : WriteAsync(action, AuditActor.Anonymous, targetType, targetId, details);
    }

    public async Task WriteAsync(string action, AuditActor actor, string? targetType = null, Guid? targetId = null, object? details = null)
    {
        await db.ExecuteAsync("""
            INSERT INTO cmdb.AuditLog (ActorType, ActorId, Action, TargetType, TargetId, Ip, Details)
            VALUES (@actorType, @actorId, @action, @targetType, @targetId, @ip, @details);
            """,
            new
            {
                actorType = actor.Type,
                actorId = actor.Id?.ToString("D"),
                action,
                targetType,
                targetId = targetId?.ToString("D"),
                ip = http.HttpContext?.ClientAddress(),
                details = details is null ? null : JsonSerializer.Serialize(details, Json),
            });
    }
}

internal sealed record AuditActor(string Type, Guid? Id)
{
    public static readonly AuditActor Anonymous = new("Anonymous", null);

    public static readonly AuditActor System = new("System", null);

    public static AuditActor User(Guid id) => new("User", id);
}

/// <summary>The audit actions of slice 3 (DEC-038 items 1, 2, 8 and 9).</summary>
internal static class AuditActions
{
    public const string SetupCompleted = "setup.completed";
    public const string SetupTokenRejected = "setup.token-rejected";

    public const string SignInSucceeded = "auth.sign-in.succeeded";
    public const string SignInFailed = "auth.sign-in.failed";
    public const string AccountLockedOut = "auth.account.locked-out";
    public const string RecoveryCodeUsed = "auth.recovery-code.used";
    public const string SignedOut = "auth.signed-out";

    public const string InvitationCreated = "user.invitation.created";
    public const string InvitationReissued = "user.invitation.reissued";
    public const string InvitationRevoked = "user.invitation.revoked";
    public const string LinkCodeFailed = "user.link.code-failed";
    public const string LinkInvalidated = "user.link.invalidated";
    public const string InvitationRedeemed = "user.invitation.redeemed";
    public const string RolesChanged = "user.roles.changed";
    public const string Locked = "user.locked";
    public const string Unlocked = "user.unlocked";
    public const string Disabled = "user.disabled";
    public const string Enabled = "user.enabled";
    public const string TwoFactorReset = "user.two-factor.reset";
    public const string TwoFactorResetCompleted = "user.two-factor.reset-completed";

    public const string ConfirmationFailed = "account.confirmation.failed";
    public const string RecoveryCodesRegenerated = "account.recovery-codes.regenerated";
    public const string AuthenticatorChanged = "account.authenticator.changed";
}

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

    public Task WriteAsync(string action, AuditActor actor, string? targetType = null, Guid? targetId = null, object? details = null) =>
        WriteCoreAsync(action, actor, targetType, targetId?.ToString("D"), details);

    /// <summary>Records <paramref name="action"/> by the signed-in person about something with a numeric ID (a CI type).</summary>
    public Task WriteAsync(string action, string targetType, int targetId, object? details)
    {
        var actorId = http.HttpContext?.User.Identity?.AuthenticationType == AuthSchemes.Session ? http.HttpContext.User.UserId() : null;
        return WriteCoreAsync(action, actorId is { } id ? AuditActor.User(id) : AuditActor.Anonymous, targetType,
            targetId.ToString(System.Globalization.CultureInfo.InvariantCulture), details);
    }

    private async Task WriteCoreAsync(string action, AuditActor actor, string? targetType, string? targetId, object? details)
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
                targetId,
                ip = http.HttpContext?.ClientAddress(),
                details = details is null ? null : JsonSerializer.Serialize(details, Json),
            });
    }
}

internal sealed record AuditActor(string Type, Guid? Id)
{
    public static readonly AuditActor Anonymous = new(AuditActorTypes.Anonymous, null);

    public static readonly AuditActor System = new(AuditActorTypes.System, null);

    public static AuditActor User(Guid id) => new(AuditActorTypes.User, id);
}

/// <summary>The audit actions of slice 3 (DEC-038 items 1, 2, 8 and 9), slice 4 (DEC-041), slice 4a (DEC-042), 4b (DEC-043), 5 (DEC-044), 5b (DEC-045) and 6 (DEC-046); <see cref="AuditCatalog"/> labels each one.</summary>
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

    // Slice 4a (DEC-042): password resets, dual control, and the person's own password change.
    public const string PasswordReset = "user.password.reset";
    public const string PasswordResetCompleted = "user.password.reset-completed";
    public const string ApprovalRequested = "user.approval.requested";
    public const string RequestApproved = "user.approval.approved";
    public const string RequestDeclined = "user.approval.declined";
    public const string RequestCancelled = "user.approval.cancelled";

    // The recovery setting's link (DEC-042 item 6): nothing changes until it is used, so not "Password reset".
    public const string RecoveryLinkIssued = "user.recovery-link.issued";

    public const string ConfirmationFailed = "account.confirmation.failed";
    public const string RecoveryCodesRegenerated = "account.recovery-codes.regenerated";
    public const string AuthenticatorChanged = "account.authenticator.changed";
    public const string PasswordChanged = "account.password.changed";

    // Slice 4 (DEC-041). The last two are written by SQL, not through AuditTrail: the purge by
    // cmdb.PurgeAuditLog (0004), a retention change by migrate.
    public const string Exported = "audit.exported";
    public const string RetentionPurged = "audit.retention.purged";
    public const string RetentionChanged = "audit.retention.changed";

    // Slice 4b (DEC-043): written by SQL, by the rotate-db-credentials command.
    public const string CredentialsRotated = "database.credentials.rotated";

    // Slice 5 (DEC-044): CIs and their types.
    public const string CiCreated = "ci.created";
    public const string CiChanged = "ci.changed";
    public const string CiRetired = "ci.retired";
    public const string CiRestored = "ci.restored";
    public const string AttributeAdded = "type.attribute.added";

    // Slice 5b (DEC-045): screen 14.
    public const string TypeAdded = "type.added";
    public const string TypeDeleted = "type.deleted";
    public const string TypeHidden = "type.hidden";
    public const string TypeShown = "type.shown";
    public const string AttributeChanged = "type.attribute.changed";
    public const string AttributeDeleted = "type.attribute.deleted";
    public const string RelationshipTypeAdded = "relationship-type.added";
    public const string RelationshipTypeChanged = "relationship-type.changed";
    public const string RelationshipTypeDeleted = "relationship-type.deleted";

    // Slice 6 (DEC-046): relationships, and the graph's CSV export (item 7; exports are audited, DEC-041 item 3; owner, 2026-10-25: named for
    // what it holds, so #47's CI export sits the same way under CIs).
    public const string RelationshipAdded = "relationship.added";
    public const string RelationshipChanged = "relationship.changed";
    public const string RelationshipDeleted = "relationship.deleted";
    public const string RelationshipsExported = "relationship.exported";
}

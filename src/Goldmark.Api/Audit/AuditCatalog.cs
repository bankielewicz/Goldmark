namespace Goldmark.Api.Audit;

/// <summary>
/// Every audit action this build writes, with the group, label and outcome screen 16 shows and the CSV
/// export uses (US-07, DEC-041). The audit log screen takes its Action filter from here, so a later
/// slice adds its actions to this list and the screen follows. A test checks that every
/// <see cref="AuditActions"/> constant is here.
/// </summary>
internal static class AuditCatalog
{
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";

    /// <summary>A protective action: an account locked after failed attempts, a link that stopped working.</summary>
    public const string Blocked = "Blocked";

    public static readonly IReadOnlyList<string> Outcomes = [Succeeded, Failed, Blocked];

    public static readonly IReadOnlyList<AuditActionInfo> Actions =
    [
        new(AuditActions.SignInSucceeded, "Sign-in", "Signed in", Succeeded),
        new(AuditActions.SignInFailed, "Sign-in", "Sign-in failed", Failed),
        new(AuditActions.AccountLockedOut, "Sign-in", "Account locked after failed attempts", Blocked),
        new(AuditActions.RecoveryCodeUsed, "Sign-in", "Recovery code used", Succeeded),
        new(AuditActions.SignedOut, "Sign-in", "Signed out", Succeeded),

        new(AuditActions.SetupCompleted, "Setup", "Setup completed", Succeeded),
        new(AuditActions.SetupTokenRejected, "Setup", "Setup token rejected", Failed),

        new(AuditActions.InvitationCreated, "Users & roles", "Invitation created", Succeeded),
        new(AuditActions.InvitationReissued, "Users & roles", "Invitation link reissued", Succeeded),
        new(AuditActions.InvitationRevoked, "Users & roles", "Invitation revoked", Succeeded),
        new(AuditActions.InvitationRedeemed, "Users & roles", "Invitation accepted", Succeeded),
        new(AuditActions.LinkCodeFailed, "Users & roles", "Link code wrong", Failed),
        new(AuditActions.LinkInvalidated, "Users & roles", "Link stopped working", Blocked),
        new(AuditActions.RolesChanged, "Users & roles", "Roles changed", Succeeded),
        new(AuditActions.Locked, "Users & roles", "Account locked", Succeeded),
        new(AuditActions.Unlocked, "Users & roles", "Account unlocked", Succeeded),
        new(AuditActions.Disabled, "Users & roles", "Account disabled", Succeeded),
        new(AuditActions.Enabled, "Users & roles", "Account enabled", Succeeded),
        new(AuditActions.TwoFactorReset, "Users & roles", "Two-factor reset", Succeeded),
        new(AuditActions.TwoFactorResetCompleted, "Users & roles", "Two-factor set up again", Succeeded),
        new(AuditActions.PasswordReset, "Users & roles", "Password reset", Succeeded),
        new(AuditActions.PasswordResetCompleted, "Users & roles", "Password reset completed", Succeeded),
        new(AuditActions.ApprovalRequested, "Users & roles", "Approval requested", Succeeded),
        new(AuditActions.RequestApproved, "Users & roles", "Request approved", Succeeded),
        new(AuditActions.RequestDeclined, "Users & roles", "Request declined", Succeeded),
        new(AuditActions.RequestCancelled, "Users & roles", "Request cancelled", Succeeded),
        new(AuditActions.RecoveryLinkIssued, "Users & roles", "Recovery link issued", Succeeded),

        new(AuditActions.ConfirmationFailed, "Own account", "Password or code wrong at confirmation", Failed),
        new(AuditActions.RecoveryCodesRegenerated, "Own account", "New recovery codes", Succeeded),
        new(AuditActions.AuthenticatorChanged, "Own account", "New authenticator", Succeeded),
        new(AuditActions.PasswordChanged, "Own account", "Password changed", Succeeded),

        new(AuditActions.Exported, "Audit log", "Audit log exported", Succeeded),
        new(AuditActions.RetentionPurged, "Audit log", "Old entries deleted", Succeeded),
        new(AuditActions.RetentionChanged, "Audit log", "Retention period changed", Succeeded),
    ];

    private static readonly Dictionary<string, AuditActionInfo> ByCode = Actions.ToDictionary(a => a.Code, StringComparer.Ordinal);

    public static AuditActionInfo? Find(string code) => ByCode.GetValueOrDefault(code);

    /// <summary>An action from a newer build is shown by its code and counted as succeeded.</summary>
    public static AuditActionInfo Describe(string code) => Find(code) ?? new AuditActionInfo(code, "Other", code, Succeeded);
}

internal sealed record AuditActionInfo(string Code, string Group, string Label, string Outcome);

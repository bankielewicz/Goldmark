using System.Text;
using Goldmark.Api.Auth;
using Goldmark.Api.Data;
using Goldmark.Api.Security;

namespace Goldmark.Api.Users;

/// <summary>A person as screen 17 lists them (US-04).</summary>
internal sealed record UserSummary(
    Guid Id,
    string UserName,
    string DisplayName,
    IReadOnlyList<string> Roles,
    bool TwoFactorEnrolled,
    UserAccountState Account,
    DateTime? LastSignInAt,
    DateTime CreatedAt,
    string ETag);

/// <summary>
/// <c>State</c>: active, locked, disabled, invitation-pending or invitation-expired. <c>LockedUntil</c> is
/// set for an automatic lockout, null for an administrator's lock; <c>Link</c> describes an open
/// invitation or reset link; <c>PasswordResetPending</c>: an administrator reset the password and the
/// person has not chosen a new one yet (DEC-042).
/// </summary>
internal sealed record UserAccountState(string State, DateTimeOffset? LockedUntil, OpenLink? Link, bool PasswordResetPending);

/// <summary>
/// An open link: its purpose (invitation, two-factor-reset, password-reset, combined-reset), when it (or,
/// awaiting approval, the request) ends, and under dual control who asked for it (DEC-042).
/// </summary>
internal sealed record OpenLink(string Purpose, DateTime ExpiresAt, bool Expired, bool AwaitingApproval, LinkRequester? RequestedBy);

/// <summary>The Admin who asked for a link under dual control, and when (DEC-042).</summary>
internal sealed record LinkRequester(Guid Id, string DisplayName, DateTime AskedAt);

/// <summary>
/// An Admin who could approve a request (DEC-042 item 8), and whether they can sign in to do it: not with
/// a password reset unfinished, without an authenticator, or locked by an administrator. The automatic
/// lockout is not counted, so failed sign-ins in someone's name can't change who approves.
/// </summary>
internal sealed record Approver(string DisplayName, bool PasswordResetPending, bool HasAuthenticator, bool LockedByAdministrator)
{
    public bool CanApprove => !PasswordResetPending && HasAuthenticator && !LockedByAdministrator;
}

/// <summary>Read queries for screen 17: every person with roles, account state and open link, in one query.</summary>
internal sealed class UserDirectory(DbSession db, TimeProvider time)
{
    private const string ListSql = """
        SELECT u.Id, u.UserName, u.DisplayName,
            CAST(CASE WHEN u.PasswordHash IS NULL THEN 1 ELSE 0 END AS bit) AS IsPending,
            CAST(CASE WHEN u.AuthenticatorKey IS NULL THEN 0 ELSE 1 END AS bit) AS TwoFactorEnrolled,
            u.LockoutEnd, u.IsDisabled, u.LastSignInAt,
            u.CreatedAt, u.ConcurrencyStamp,
            (SELECT STRING_AGG(r.Role, ',') FROM cmdb.UserRole AS r WHERE r.UserId = u.Id) AS Roles,
            CAST(CASE WHEN u.PasswordResetAt IS NULL THEN 0 ELSE 1 END AS bit) AS PasswordResetPending,
            i.Purpose AS LinkPurpose, i.ExpiresAt AS LinkExpiresAt,
            CAST(CASE WHEN i.ApprovalRequired = 1 AND i.ApprovedAt IS NULL THEN 1 ELSE 0 END AS bit) AS LinkAwaitingApproval,
            i.CreatedBy AS LinkCreatedBy, c.DisplayName AS LinkCreatedByName, i.CreatedAt AS LinkCreatedAt
        FROM cmdb.UserAccount AS u
        LEFT JOIN cmdb.UserInvitation AS i ON i.UserId = u.Id AND i.RedeemedAt IS NULL AND i.RevokedAt IS NULL
        LEFT JOIN cmdb.UserAccount AS c ON c.Id = i.CreatedBy
        """;

    public async Task<IReadOnlyList<UserSummary>> ListAsync() =>
        [.. (await db.QueryAsync<Row>(ListSql + " ORDER BY u.DisplayName, u.UserName;")).Select(Summarize)];

    public async Task<UserSummary?> FindAsync(Guid id) =>
        await db.QuerySingleOrDefaultAsync<Row>(ListSql + " WHERE u.Id = @id;", new { id }) is { } row ? Summarize(row) : null;

    /// <summary>
    /// Enabled people holding Admin, read with update locks held to the end of the caller's
    /// serializable transaction, so two administrators cannot both remove the last Admin.
    /// </summary>
    public Task<int> CountEnabledAdministratorsAsync() =>
        db.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM cmdb.UserRole AS r WITH (UPDLOCK, HOLDLOCK)
            JOIN cmdb.UserAccount AS u WITH (UPDLOCK, HOLDLOCK) ON u.Id = r.UserId
            WHERE r.Role = 'Admin' AND u.IsDisabled = 0 AND u.PasswordHash IS NOT NULL;
            """);

    /// <summary>
    /// Administrators who could approve a request about <paramref name="target"/> asked by
    /// <paramref name="requester"/> (DEC-042): enabled, past their invitation, and neither of the two,
    /// with whether each can sign in to approve. None means the one who asks gets both the link and the
    /// code; none who can sign in means the request is refused (owner: "C. Refuse, say why").
    /// </summary>
    public Task<IReadOnlyList<Approver>> ApproversAsync(Guid requester, Guid target) =>
        db.QueryAsync<Approver>("""
            SELECT u.DisplayName,
                CAST(CASE WHEN u.PasswordResetAt IS NULL THEN 0 ELSE 1 END AS bit) AS PasswordResetPending,
                CAST(CASE WHEN u.AuthenticatorKey IS NULL THEN 0 ELSE 1 END AS bit) AS HasAuthenticator,
                CAST(CASE WHEN u.LockoutEnd = @administratorLock THEN 1 ELSE 0 END AS bit) AS LockedByAdministrator
            FROM cmdb.UserRole AS r
            JOIN cmdb.UserAccount AS u ON u.Id = r.UserId
            WHERE r.Role = 'Admin' AND u.IsDisabled = 0 AND u.PasswordHash IS NOT NULL AND u.Id <> @requester AND u.Id <> @target
            ORDER BY u.DisplayName;
            """, new { requester, target, administratorLock = DateTimeOffset.MaxValue });

    /// <summary>Requests <paramref name="admin"/> could approve now: waiting, not ended, not their own, not about them.</summary>
    public Task<int> CountApprovalsWaitingForAsync(Guid admin) =>
        db.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM cmdb.UserInvitation AS i
            JOIN cmdb.UserAccount AS u ON u.Id = i.UserId
            WHERE i.ApprovalRequired = 1 AND i.ApprovedAt IS NULL AND i.RedeemedAt IS NULL AND i.RevokedAt IS NULL
                AND i.ExpiresAt > @now AND u.IsDisabled = 0 AND (i.CreatedBy IS NULL OR i.CreatedBy <> @admin) AND i.UserId <> @admin;
            """, new { admin, now = time.GetUtcNow().UtcDateTime });

    /// <summary>The list as CSV for "Export CSV", formula cells neutralized (<see cref="Csv"/>).</summary>
    public static string ToCsv(IEnumerable<UserSummary> users)
    {
        var csv = new StringBuilder("Name,Username,Roles,Two-factor,Account,Last sign-in (UTC)\r\n");
        foreach (var user in users)
        {
            string[] cells =
            [
                user.DisplayName,
                user.UserName,
                string.Join(' ', user.Roles),
                user.TwoFactorEnrolled ? "Enrolled" : "Not enrolled",
                user.Account.State,
                user.LastSignInAt?.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) ?? "",
            ];
            csv.AppendRow(cells);
        }

        return csv.ToString();
    }

    private UserSummary Summarize(Row row)
    {
        var now = time.GetUtcNow();
        var link = row.LinkPurpose is { } purpose && row.LinkExpiresAt is { } expires
            ? new OpenLink(PurposeName(purpose), expires, expires <= now.UtcDateTime, row.LinkAwaitingApproval,
                row.LinkAwaitingApproval && row.LinkCreatedBy is { } by && row.LinkCreatedAt is { } asked
                    ? new LinkRequester(by, row.LinkCreatedByName ?? "", asked)
                    : null)
            : null;
        var state = row switch
        {
            { IsDisabled: true } => "disabled",
            { IsPending: true } => link is { Expired: false } ? "invitation-pending" : "invitation-expired",
            { LockoutEnd: { } end } when end > now => "locked",
            _ => "active",
        };
        var lockedUntil = state == "locked" && row.LockoutEnd != DateTimeOffset.MaxValue ? row.LockoutEnd : null;
        var roles = (row.Roles ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        return new UserSummary(row.Id, row.UserName, row.DisplayName, [.. Roles.ForPeople.Where(r => roles.Contains(r, StringComparer.Ordinal))],
            row.TwoFactorEnrolled, new UserAccountState(state, lockedUntil, link, row.PasswordResetPending), row.LastSignInAt, row.CreatedAt,
            row.ConcurrencyStamp);
    }

    private static string PurposeName(string purpose) => purpose switch
    {
        LinkPurpose.Invitation => "invitation",
        LinkPurpose.PasswordReset => "password-reset",
        LinkPurpose.CombinedReset => "combined-reset",
        _ => "two-factor-reset",
    };

    private sealed class Row
    {
        public Guid Id { get; set; }

        public string UserName { get; set; } = "";

        public string DisplayName { get; set; } = "";

        public bool IsPending { get; set; }

        public bool TwoFactorEnrolled { get; set; }

        public DateTimeOffset? LockoutEnd { get; set; }

        public bool IsDisabled { get; set; }

        public DateTime? LastSignInAt { get; set; }

        public DateTime CreatedAt { get; set; }

        public string ConcurrencyStamp { get; set; } = "";

        public string? Roles { get; set; }

        public string? LinkPurpose { get; set; }

        public DateTime? LinkExpiresAt { get; set; }

        public bool PasswordResetPending { get; set; }

        public bool LinkAwaitingApproval { get; set; }

        public Guid? LinkCreatedBy { get; set; }

        public string? LinkCreatedByName { get; set; }

        public DateTime? LinkCreatedAt { get; set; }
    }
}

using System.Text;
using Goldmark.Api.Auth;
using Goldmark.Api.Data;

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
/// invitation or two-factor reset link.
/// </summary>
internal sealed record UserAccountState(string State, DateTimeOffset? LockedUntil, OpenLink? Link);

internal sealed record OpenLink(string Purpose, DateTime ExpiresAt, bool Expired);

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
            i.Purpose AS LinkPurpose, i.ExpiresAt AS LinkExpiresAt
        FROM cmdb.UserAccount AS u
        LEFT JOIN cmdb.UserInvitation AS i ON i.UserId = u.Id AND i.RedeemedAt IS NULL AND i.RevokedAt IS NULL
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
    /// The list as CSV for "Export CSV". A cell that a spreadsheet would read as a formula (starting
    /// with =, +, -, @, a tab or a carriage return) is prefixed with an apostrophe.
    /// </summary>
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
            csv.AppendJoin(',', cells.Select(Cell)).Append("\r\n");
        }

        return csv.ToString();
    }

    private static string Cell(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 || value.StartsWith('\'') ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
    }

    private UserSummary Summarize(Row row)
    {
        var now = time.GetUtcNow();
        var link = row.LinkPurpose is { } purpose && row.LinkExpiresAt is { } expires
            ? new OpenLink(purpose == LinkPurpose.Invitation ? "invitation" : "two-factor-reset", expires, expires <= now.UtcDateTime)
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
            row.TwoFactorEnrolled, new UserAccountState(state, lockedUntil, link), row.LastSignInAt, row.CreatedAt, row.ConcurrencyStamp);
    }

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
    }
}

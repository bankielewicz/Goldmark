using System.Security.Cryptography;
using Goldmark.Api.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Goldmark.Api.Auth;

internal static class LinkPurpose
{
    public const string Invitation = "Invitation";
    public const string TwoFactorReset = "TwoFactorReset";

    /// <summary>A new password; the person keeps their authenticator and proves it at the end (DEC-042).</summary>
    public const string PasswordReset = "PasswordReset";

    /// <summary>A new password and a new authenticator: the invitation's steps (DEC-042).</summary>
    public const string CombinedReset = "CombinedReset";
}

/// <summary>Why a link closed before it was used (<c>RevokeReason</c>, migration 0005).</summary>
internal static class LinkClosure
{
    public const string Replaced = "Replaced";
    public const string Revoked = "Revoked";
    public const string CodeAttempts = "CodeAttempts";
    public const string Declined = "Declined";
    public const string Cancelled = "Cancelled";
}

/// <summary>A link as stored (<c>cmdb.UserInvitation</c>): hashes only, and the code's key encrypted.</summary>
internal sealed record LinkRow(
    Guid Id, Guid UserId, string Purpose, byte[]? CodeHash, byte CodeHashScheme, byte[]? CodeKey, byte FailedCodeAttempts,
    DateTime ExpiresAt, DateTime? RedeemedAt, DateTime? RevokedAt, bool ApprovalRequired, DateTime? ApprovedAt, Guid? ApprovedBy,
    Guid? CreatedBy, DateTime CreatedAt)
{
    public bool IsOpen => RedeemedAt is null && RevokedAt is null;

    /// <summary>Asked for under dual control and not approved yet: it has no code, so it cannot be used (DEC-042).</summary>
    public bool IsAwaitingApproval => ApprovalRequired && ApprovedAt is null;

    /// <summary>
    /// Issued by the recovery setting (DEC-042 item 6), the one link no account issues: its code is the
    /// operator's (<see cref="LinkSecrets.NormalizeRecoveryCode"/>), not a generated one.
    /// </summary>
    public bool IsRecovery => CreatedBy is null;
}

/// <summary>
/// A link as shown once to the administrator: the token goes in the URL fragment, the code is said
/// aloud. Carries the configured limits, so the screen states them as they are (Codex, PR #24).
/// </summary>
internal sealed record IssuedLink(string Token, string Code, DateTime ExpiresAt, int ExpiryHours, int CodeAttempts);

/// <summary>A link asked for under dual control, shown once to the one who asked: the token only (DEC-042).</summary>
internal sealed record RequestedLink(string Token, DateTime ExpiresAt, int ExpiryHours, int CodeAttempts);

/// <summary>A recovery link (DEC-042 item 6): its token only, since the operator chose its code.</summary>
internal sealed record RecoveryLink(string Token, DateTime ExpiresAt, int CodeAttempts);

/// <summary>The code of an approved link, shown once to the approving administrator (DEC-042).</summary>
internal sealed record ApprovedCode(string Code, DateTime ExpiresAt, int ExpiryHours, int CodeAttempts);

/// <summary>
/// Invitation and reset links (DEC-038 items 2 and 8, DEC-042): issued, checked, failed codes counted
/// (the link is revoked at <see cref="AuthOptions.LinkCodeAttempts"/>), redeemed once, revoked. Under
/// dual control a link is first requested (a token, no code) and gets its code when another
/// administrator approves it; that code is hashed under a random key of the link's own, encrypted with
/// Data Protection, since the token is no longer known (scheme 2, migration 0005). Runs in the
/// caller's transaction.
/// </summary>
internal sealed class LinkStore(DbSession db, IOptions<AuthOptions> options, TimeProvider time, IDataProtectionProvider protection)
{
    public const string CodeKeyPurpose = "Goldmark.LinkCodeKey.v1";

    private const string Columns = """
        Id, UserId, Purpose, CodeHash, CodeHashScheme, CodeKey, FailedCodeAttempts, ExpiresAt, RedeemedAt, RevokedAt,
        ApprovalRequired, ApprovedAt, ApprovedBy, CreatedBy, CreatedAt
        """;

    private readonly IDataProtector _codeKeys = protection.CreateProtector(CodeKeyPurpose);

    public Task<LinkRow?> FindAsync(string token) =>
        db.QuerySingleOrDefaultAsync<LinkRow>("SELECT " + Columns + " FROM cmdb.UserInvitation WHERE TokenHash = @hash;",
            new { hash = LinkSecrets.HashToken(token) });

    public Task<LinkRow?> FindAsync(Guid id) =>
        db.QuerySingleOrDefaultAsync<LinkRow>("SELECT " + Columns + " FROM cmdb.UserInvitation WHERE Id = @id;", new { id });

    /// <summary>The person's open link, if any (expired or not, approved or not).</summary>
    public Task<LinkRow?> OpenForUserAsync(Guid userId) =>
        db.QuerySingleOrDefaultAsync<LinkRow>(
            "SELECT " + Columns + " FROM cmdb.UserInvitation WHERE UserId = @userId AND RedeemedAt IS NULL AND RevokedAt IS NULL;",
            new { userId });

    public bool IsExpired(LinkRow link) => link.ExpiresAt <= Now;

    public int TriesLeft(LinkRow link) => Math.Max(0, options.Value.LinkCodeAttempts - link.FailedCodeAttempts);

    /// <summary>
    /// Revokes the person's open link, if any, and issues a new one with its code. <paramref name="createdBy"/>
    /// is never null here: a recovery link is <see cref="IssueRecoveryAsync"/>'s.
    /// </summary>
    public async Task<IssuedLink> IssueAsync(Guid userId, string purpose, Guid createdBy)
    {
        var token = LinkSecrets.NewToken();
        var code = LinkSecrets.NewCode();
        var now = Now;
        var expires = now + options.Value.LinkExpiry;
        await RevokeOpenAsync(userId, LinkClosure.Replaced, createdBy);
        await db.ExecuteAsync("""
            INSERT INTO cmdb.UserInvitation (Id, UserId, Purpose, TokenHash, CodeHash, CreatedAt, CreatedBy, ExpiresAt)
            VALUES (@id, @userId, @purpose, @tokenHash, @codeHash, @now, @createdBy, @expires);
            """,
            new
            {
                id = Guid.CreateVersion7(),
                userId,
                purpose,
                tokenHash = LinkSecrets.HashToken(token),
                codeHash = LinkSecrets.HashCode(token, LinkSecrets.NormalizeCode(code)!),
                now,
                createdBy,
                expires,
            });
        return new IssuedLink(token, code, expires, options.Value.LinkExpiryHours, options.Value.LinkCodeAttempts);
    }

    /// <summary>How long a recovery link works (owner, DEC-042 item 6: "1 hour, ended at a clean restart").</summary>
    public static readonly TimeSpan RecoveryLinkLifetime = TimeSpan.FromHours(1);

    /// <summary>
    /// Revokes the person's open link, if any, and issues a password-and-two-factor link for the recovery
    /// setting (DEC-042 item 6): no issuing account, the operator's code (normalized), one hour.
    /// </summary>
    public async Task<RecoveryLink> IssueRecoveryAsync(Guid userId, string normalizedCode)
    {
        var token = LinkSecrets.NewToken();
        var now = Now;
        var expires = now + RecoveryLinkLifetime;
        await RevokeOpenAsync(userId, LinkClosure.Replaced);
        await db.ExecuteAsync("""
            INSERT INTO cmdb.UserInvitation (Id, UserId, Purpose, TokenHash, CodeHash, CreatedAt, CreatedBy, ExpiresAt)
            VALUES (@id, @userId, 'CombinedReset', @tokenHash, @codeHash, @now, NULL, @expires);
            """,
            new
            {
                id = Guid.CreateVersion7(),
                userId,
                tokenHash = LinkSecrets.HashToken(token),
                codeHash = LinkSecrets.HashCode(token, normalizedCode),
                now,
                expires,
            });
        return new RecoveryLink(token, expires, options.Value.LinkCodeAttempts);
    }

    /// <summary>
    /// Revokes every unused recovery link (at each start, DEC-042 item 6), so a recovery link lasts at most
    /// until the next start and emptying the setting and restarting ends a copy left in a log. Returns whose
    /// they were.
    /// </summary>
    public Task<IReadOnlyList<Guid>> RevokeRecoveryLinksAsync() =>
        db.QueryAsync<Guid>("""
            UPDATE cmdb.UserInvitation SET RevokedAt = @now, RevokeReason = 'Revoked'
            OUTPUT inserted.UserId
            WHERE CreatedBy IS NULL AND Purpose = 'CombinedReset' AND RedeemedAt IS NULL AND RevokedAt IS NULL;
            """, new { now = Now });

    /// <summary>
    /// Revokes the person's open link, if any, and asks for a new one under dual control: a token and no
    /// code. The request ends at <see cref="RequestedLink.ExpiresAt"/> unless another administrator approves it.
    /// </summary>
    public async Task<RequestedLink> RequestAsync(Guid userId, string purpose, Guid createdBy)
    {
        var token = LinkSecrets.NewToken();
        var now = Now;
        var expires = now + options.Value.LinkExpiry;
        await RevokeOpenAsync(userId, LinkClosure.Replaced, createdBy);
        await db.ExecuteAsync("""
            INSERT INTO cmdb.UserInvitation (Id, UserId, Purpose, TokenHash, CodeHash, ApprovalRequired, CreatedAt, CreatedBy, ExpiresAt)
            VALUES (@id, @userId, @purpose, @tokenHash, NULL, 1, @now, @createdBy, @expires);
            """,
            new { id = Guid.CreateVersion7(), userId, purpose, tokenHash = LinkSecrets.HashToken(token), now, createdBy, expires });
        return new RequestedLink(token, expires, options.Value.LinkExpiryHours, options.Value.LinkCodeAttempts);
    }

    /// <summary>
    /// Approves a waiting request: makes its code and starts the link's own lifetime. Null if the
    /// request was no longer waiting (approved, declined, cancelled, replaced or ended meanwhile).
    /// </summary>
    public async Task<ApprovedCode?> ApproveAsync(Guid linkId, Guid approvedBy)
    {
        var code = LinkSecrets.NewCode();
        var key = LinkSecrets.NewCodeKey();
        var now = Now;
        var expires = now + options.Value.LinkExpiry;
        var rows = await db.ExecuteAsync("""
            UPDATE cmdb.UserInvitation
            SET CodeHash = @codeHash, CodeHashScheme = 2, CodeKey = @codeKey, ApprovedAt = @now, ApprovedBy = @approvedBy, ExpiresAt = @expires
            WHERE Id = @linkId AND RedeemedAt IS NULL AND RevokedAt IS NULL AND ApprovalRequired = 1 AND ApprovedAt IS NULL
                AND ExpiresAt > @now;
            """,
            new
            {
                linkId,
                codeHash = LinkSecrets.HashCode(key, LinkSecrets.NormalizeCode(code)!),
                codeKey = _codeKeys.Protect(key),
                now,
                approvedBy,
                expires,
            });
        return rows == 1 ? new ApprovedCode(code, expires, options.Value.LinkExpiryHours, options.Value.LinkCodeAttempts) : null;
    }

    /// <summary>Closes a waiting request (declined by another administrator, or cancelled by the one who asked); false if it was no longer waiting.</summary>
    public async Task<bool> CloseRequestAsync(Guid linkId, Guid closedBy, string reason) =>
        await db.ExecuteAsync("""
            UPDATE cmdb.UserInvitation SET RevokedAt = @now, RevokedBy = @closedBy, RevokeReason = @reason
            WHERE Id = @linkId AND RedeemedAt IS NULL AND RevokedAt IS NULL AND ApprovalRequired = 1 AND ApprovedAt IS NULL;
            """, new { linkId, closedBy, reason, now = Now }) == 1;

    /// <summary>
    /// Whether <paramref name="normalizedCode"/> is the link's code. A link without a code (awaiting
    /// approval) never matches. A scheme-2 key that the key ring can no longer decrypt fails like a
    /// wrong code, as an unreadable authenticator key does (DEC-038 item 5).
    /// </summary>
    public bool CodeMatches(LinkRow link, string token, string normalizedCode)
    {
        if (link.CodeHash is not { } expected)
        {
            return false;
        }

        if (link.CodeHashScheme == 1)
        {
            return LinkSecrets.Matches(expected, LinkSecrets.HashCode(token, normalizedCode));
        }

        byte[] key;
        try
        {
            key = _codeKeys.Unprotect(link.CodeKey!);
        }
        catch (CryptographicException)
        {
            return false;
        }

        return LinkSecrets.Matches(expected, LinkSecrets.HashCode(key, normalizedCode));
    }

    /// <summary>
    /// Counts a wrong code; at the limit the link is revoked. Returns the tries left (0 = revoked now),
    /// or null if the link was no longer open or has no code yet.
    /// </summary>
    public async Task<int?> RecordWrongCodeAsync(LinkRow link)
    {
        var attempts = await db.QuerySingleOrDefaultAsync<byte?>("""
            UPDATE cmdb.UserInvitation
            SET FailedCodeAttempts = FailedCodeAttempts + 1,
                RevokedAt = CASE WHEN FailedCodeAttempts + 1 >= @limit THEN @now ELSE NULL END,
                RevokeReason = CASE WHEN FailedCodeAttempts + 1 >= @limit THEN 'CodeAttempts' ELSE NULL END
            OUTPUT inserted.FailedCodeAttempts
            WHERE Id = @id AND RedeemedAt IS NULL AND RevokedAt IS NULL AND CodeHash IS NOT NULL;
            """, new { id = link.Id, limit = options.Value.LinkCodeAttempts, now = Now });
        return attempts is { } count ? Math.Max(0, options.Value.LinkCodeAttempts - count) : null;
    }

    /// <summary>Marks the link used, if it is still open, has its code and has not expired; false otherwise.</summary>
    public async Task<bool> RedeemAsync(Guid linkId) =>
        await db.ExecuteAsync("""
            UPDATE cmdb.UserInvitation SET RedeemedAt = @now
            WHERE Id = @linkId AND RedeemedAt IS NULL AND RevokedAt IS NULL AND CodeHash IS NOT NULL AND ExpiresAt > @now;
            """, new { linkId, now = Now }) == 1;

    public Task<int> RevokeOpenAsync(Guid userId, string reason = LinkClosure.Revoked, Guid? revokedBy = null) =>
        db.ExecuteAsync("""
            UPDATE cmdb.UserInvitation SET RevokedAt = @now, RevokedBy = @revokedBy, RevokeReason = @reason
            WHERE UserId = @userId AND RedeemedAt IS NULL AND RevokedAt IS NULL;
            """, new { userId, reason, revokedBy, now = Now });

    private DateTime Now => time.GetUtcNow().UtcDateTime;
}

using Goldmark.Api.Data;
using Microsoft.Extensions.Options;

namespace Goldmark.Api.Auth;

internal static class LinkPurpose
{
    public const string Invitation = "Invitation";
    public const string TwoFactorReset = "TwoFactorReset";
}

/// <summary>A link as stored (<c>cmdb.UserInvitation</c>): hashes only.</summary>
internal sealed record LinkRow(
    Guid Id, Guid UserId, string Purpose, byte[] CodeHash, byte FailedCodeAttempts, DateTime ExpiresAt, DateTime? RedeemedAt, DateTime? RevokedAt)
{
    public bool IsOpen => RedeemedAt is null && RevokedAt is null;
}

/// <summary>
/// A link as shown once to the administrator: the token goes in the URL fragment, the code is said
/// aloud. Carries the configured limits, so the screen states them as they are (Codex, PR #24).
/// </summary>
internal sealed record IssuedLink(string Token, string Code, DateTime ExpiresAt, int ExpiryHours, int CodeAttempts);

/// <summary>
/// Invitation and two-factor reset links (DEC-038 items 2 and 8): issued, checked, failed codes
/// counted (the link is revoked at <see cref="AuthOptions.LinkCodeAttempts"/>), redeemed once,
/// revoked. Runs in the caller's transaction.
/// </summary>
internal sealed class LinkStore(DbSession db, IOptions<AuthOptions> options, TimeProvider time)
{
    public Task<LinkRow?> FindAsync(string token) =>
        db.QuerySingleOrDefaultAsync<LinkRow>("""
            SELECT Id, UserId, Purpose, CodeHash, FailedCodeAttempts, ExpiresAt, RedeemedAt, RevokedAt
            FROM cmdb.UserInvitation WHERE TokenHash = @hash;
            """, new { hash = LinkSecrets.HashToken(token) });

    public Task<LinkRow?> FindAsync(Guid id) =>
        db.QuerySingleOrDefaultAsync<LinkRow>("""
            SELECT Id, UserId, Purpose, CodeHash, FailedCodeAttempts, ExpiresAt, RedeemedAt, RevokedAt
            FROM cmdb.UserInvitation WHERE Id = @id;
            """, new { id });

    /// <summary>The person's open link, if any (expired or not).</summary>
    public Task<LinkRow?> OpenForUserAsync(Guid userId) =>
        db.QuerySingleOrDefaultAsync<LinkRow>("""
            SELECT Id, UserId, Purpose, CodeHash, FailedCodeAttempts, ExpiresAt, RedeemedAt, RevokedAt
            FROM cmdb.UserInvitation WHERE UserId = @userId AND RedeemedAt IS NULL AND RevokedAt IS NULL;
            """, new { userId });

    public bool IsExpired(LinkRow link) => link.ExpiresAt <= Now;

    public int TriesLeft(LinkRow link) => Math.Max(0, options.Value.LinkCodeAttempts - link.FailedCodeAttempts);

    /// <summary>Revokes the person's open link, if any, and issues a new one.</summary>
    public async Task<IssuedLink> IssueAsync(Guid userId, string purpose, Guid createdBy)
    {
        var token = LinkSecrets.NewToken();
        var code = LinkSecrets.NewCode();
        var now = Now;
        var expires = now + options.Value.LinkExpiry;
        await RevokeOpenAsync(userId);
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

    /// <summary>
    /// Counts a wrong code; at the limit the link is revoked. Returns the tries left (0 = revoked now),
    /// or null if the link was no longer open.
    /// </summary>
    public async Task<int?> RecordWrongCodeAsync(LinkRow link)
    {
        var attempts = await db.QuerySingleOrDefaultAsync<byte?>("""
            UPDATE cmdb.UserInvitation
            SET FailedCodeAttempts = FailedCodeAttempts + 1,
                RevokedAt = CASE WHEN FailedCodeAttempts + 1 >= @limit THEN @now ELSE NULL END
            OUTPUT inserted.FailedCodeAttempts
            WHERE Id = @id AND RedeemedAt IS NULL AND RevokedAt IS NULL;
            """, new { id = link.Id, limit = options.Value.LinkCodeAttempts, now = Now });
        return attempts is { } count ? Math.Max(0, options.Value.LinkCodeAttempts - count) : null;
    }

    /// <summary>Marks the link used, if it is still open and not expired; false otherwise.</summary>
    public async Task<bool> RedeemAsync(Guid linkId) =>
        await db.ExecuteAsync("""
            UPDATE cmdb.UserInvitation SET RedeemedAt = @now
            WHERE Id = @linkId AND RedeemedAt IS NULL AND RevokedAt IS NULL AND ExpiresAt > @now;
            """, new { linkId, now = Now }) == 1;

    public Task<int> RevokeOpenAsync(Guid userId) =>
        db.ExecuteAsync("UPDATE cmdb.UserInvitation SET RevokedAt = @now WHERE UserId = @userId AND RedeemedAt IS NULL AND RevokedAt IS NULL;",
            new { userId, now = Now });

    private DateTime Now => time.GetUtcNow().UtcDateTime;
}

using System.Text.Json;
using Goldmark.Api.Data;
using Goldmark.Api.Inventory;

namespace Goldmark.Api.Tokens;

/// <summary>A person named on a token: who created it, or who revoked it.</summary>
internal sealed record ApiTokenPerson(Guid Id, string Name, string UserName);

/// <summary>
/// An API token as screen 18 lists it (US-06, DEC-048), never with its secret. <c>Status</c> is <c>revoked</c>,
/// <c>expired</c> or <c>active</c>, judged by the database's clock as every moment Goldmark stores (DEC-047 item 2).
/// </summary>
internal sealed record ApiTokenSummary(Guid Id, string Name, string LastFour, IReadOnlyList<string> Scopes, DateTime CreatedAt,
    ApiTokenPerson CreatedBy, DateTime ExpiresAt, DateTime? LastUsedAt, DateTime? RevokedAt, ApiTokenPerson? RevokedBy, string Status);

/// <summary>A token a request presents that is active now: its ID, name and scopes.</summary>
internal sealed record ActiveApiToken(Guid Id, string Name, IReadOnlyList<string> Scopes);

internal static class ApiTokenStatuses
{
    public const string Active = "active";
    public const string Expired = "expired";
    public const string Revoked = "revoked";
}

/// <summary>
/// Reads and writes <c>cmdb.ApiToken</c> (0010; US-06, DEC-048). A request's token is found by the SHA-256 of its
/// secret, and only while it is neither revoked nor expired by the database's clock, so a revoked token stops working
/// with the next request. Tokens are never deleted: a revoked or expired one keeps its name for the audit log and the
/// inventory's histories.
/// </summary>
internal sealed class ApiTokenStore(DbSession db)
{
    // The binary(32) cast keeps the comparison on the unique index (a byte[] parameter arrives as varbinary).
    private const string FindActiveSql = """
        SELECT t.Id, t.Name, t.Scopes
        FROM cmdb.ApiToken AS t
        WHERE t.Hash = CAST(@hash AS binary(32)) AND t.RevokedAt IS NULL AND t.ExpiresAt > SYSUTCDATETIME();
        """;

    // DEC-048: the last-used time at most once a minute, and "API token used" at most once an hour, both decided by this
    // one conditional UPDATE, so of concurrent requests one writes and the others find the row just written. No row
    // comes back when nothing was due; otherwise whether the use is due in the audit log.
    private const string TouchSql = """
        DECLARE @now datetime2(7) = SYSUTCDATETIME();
        UPDATE cmdb.ApiToken
        SET LastUsedAt = @now,
            UseRecordedAt = IIF(UseRecordedAt IS NULL OR UseRecordedAt <= DATEADD(hour, -1, @now), @now, UseRecordedAt)
        OUTPUT CAST(IIF(deleted.UseRecordedAt IS NULL OR deleted.UseRecordedAt <= DATEADD(hour, -1, @now), 1, 0) AS bit)
        WHERE Id = @id AND (LastUsedAt IS NULL OR LastUsedAt < DATEADD(minute, -1, @now));
        """;

    private const string Columns = """
        SELECT t.Id, t.Name, t.LastFour, t.Scopes, t.CreatedAt, t.CreatedBy, c.DisplayName AS CreatedByName, c.UserName AS CreatedByUserName,
            t.ExpiresAt, t.LastUsedAt, t.RevokedAt, t.RevokedBy, r.DisplayName AS RevokedByName, r.UserName AS RevokedByUserName,
            CASE WHEN t.RevokedAt IS NOT NULL THEN 'revoked' WHEN t.ExpiresAt <= SYSUTCDATETIME() THEN 'expired' ELSE 'active' END AS Status
        FROM cmdb.ApiToken AS t
        JOIN cmdb.UserAccount AS c ON c.Id = t.CreatedBy
        LEFT JOIN cmdb.UserAccount AS r ON r.Id = t.RevokedBy
        """;

    // Newest first (screen 18).
    private const string ListSql = Columns + "\nORDER BY t.CreatedAt DESC, t.Id DESC;";

    private const string FindSql = Columns + "\nWHERE t.Id = @id;";

    private const string NowSql = "SELECT SYSUTCDATETIME();";

    // Held to the end of the creating transaction, so two Admins giving two tokens one name can't both see it free.
    private const string NameTakenSql = """
        SELECT CAST(COUNT(*) AS bit) FROM cmdb.ApiToken WITH (UPDLOCK, HOLDLOCK) WHERE NormalizedName = @normalizedName;
        """;

    // Times travel as text and are converted as datetime2(7): a DateTime parameter would be sent as datetime (CiStore).
    private const string InsertSql = """
        INSERT INTO cmdb.ApiToken (Id, Name, NormalizedName, Hash, LastFour, Scopes, CreatedAt, CreatedBy, ExpiresAt)
        VALUES (@id, @name, @normalizedName, @hash, @lastFour, @scopes, CONVERT(datetime2(7), @createdAt, 126), @createdBy,
            CONVERT(datetime2(7), @expiresAt, 126));
        """;

    // Only an active token is revoked: a revoked or expired one stays as it is.
    private const string RevokeSql = """
        UPDATE cmdb.ApiToken SET RevokedAt = SYSUTCDATETIME(), RevokedBy = @revokedBy
        WHERE Id = @id AND RevokedAt IS NULL AND ExpiresAt > SYSUTCDATETIME();
        """;

    /// <summary>The unique index on the normalized name, which a concurrent insert of the same name runs into.</summary>
    public const string NameIndex = "UQ_ApiToken_NormalizedName";

    /// <summary>The token whose secret hashes to <paramref name="hash"/>, if it is active now.</summary>
    public async Task<ActiveApiToken?> FindActiveAsync(byte[] hash, CancellationToken cancellationToken = default) =>
        await db.QuerySingleOrDefaultAsync<ActiveRow>(FindActiveSql, new { hash }, cancellationToken) is { } row
            ? new ActiveApiToken(row.Id, row.Name, ScopesOf(row.Scopes))
            : null;

    /// <summary>
    /// Records that the token was used now, if it wasn't in the last minute; true when its use is also due in the audit
    /// log (not in the last hour), false when only the time was written, null when nothing was.
    /// </summary>
    public Task<bool?> TouchAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.QuerySingleOrDefaultAsync<bool?>(TouchSql, new { id }, cancellationToken);

    public async Task<IReadOnlyList<ApiTokenSummary>> ListAsync() => [.. (await db.QueryAsync<Row>(ListSql)).Select(Summarize)];

    public async Task<ApiTokenSummary?> FindAsync(Guid id) =>
        await db.QuerySingleOrDefaultAsync<Row>(FindSql, new { id }) is { } row ? Summarize(row) : null;

    /// <summary>The database's clock, UTC: what a new token's creation and expiry are counted from.</summary>
    public async Task<DateTime> NowAsync() => DateTime.SpecifyKind(await db.ExecuteScalarAsync<DateTime>(NowSql), DateTimeKind.Utc);

    /// <summary>Whether a token has this name already (in its normalized form), locked until the caller's transaction ends.</summary>
    public Task<bool> NameTakenAsync(string normalizedName) => db.ExecuteScalarAsync<bool>(NameTakenSql, new { normalizedName });

    public Task InsertAsync(Guid id, string name, string secret, IReadOnlyList<string> scopes, DateTime createdAt, Guid createdBy, DateTime expiresAt) =>
        db.ExecuteAsync(InsertSql, new
        {
            id,
            name,
            normalizedName = ApiTokenNames.Normalize(name),
            hash = ApiTokenSecret.Hash(secret),
            lastFour = ApiTokenSecret.LastFour(secret),
            scopes = JsonSerializer.Serialize(scopes),
            createdAt = CiStore.Stamp(createdAt),
            createdBy,
            expiresAt = CiStore.Stamp(expiresAt),
        });

    /// <summary>Revokes the token if it is active; false when there is no such token, or it is revoked or expired already.</summary>
    public async Task<bool> RevokeAsync(Guid id, Guid revokedBy) => await db.ExecuteAsync(RevokeSql, new { id, revokedBy }) == 1;

    /// <summary>The stored scopes this build knows, in its order (a scope of a later build is ignored, never granted).</summary>
    private static IReadOnlyList<string> ScopesOf(string json) =>
        JsonSerializer.Deserialize<string[]>(json) is { } stored ? [.. TokenScopes.All.Where(s => stored.Contains(s, StringComparer.Ordinal))] : [];

    private static ApiTokenSummary Summarize(Row row) =>
        new(row.Id, row.Name, row.LastFour, ScopesOf(row.Scopes), row.CreatedAt, new ApiTokenPerson(row.CreatedBy, row.CreatedByName, row.CreatedByUserName),
            row.ExpiresAt, row.LastUsedAt, row.RevokedAt,
            row.RevokedBy is { } by ? new ApiTokenPerson(by, row.RevokedByName ?? "", row.RevokedByUserName ?? "") : null, row.Status);

    private sealed record ActiveRow(Guid Id, string Name, string Scopes);

    private sealed record Row(Guid Id, string Name, string LastFour, string Scopes, DateTime CreatedAt, Guid CreatedBy, string CreatedByName,
        string CreatedByUserName, DateTime ExpiresAt, DateTime? LastUsedAt, DateTime? RevokedAt, Guid? RevokedBy, string? RevokedByName,
        string? RevokedByUserName, string Status);
}

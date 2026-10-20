using System.Security.Cryptography;
using Goldmark.Api.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace Goldmark.Api.Auth;

/// <summary>
/// ASP.NET Core Identity's store over <c>cmdb.UserAccount</c>, <c>cmdb.UserRole</c> and
/// <c>cmdb.RecoveryCode</c> (migration 0003), with Dapper through <see cref="DbSession"/> (DEC-038 4B).
/// Identity's user manager keeps the logic (password hashing and validation, lockout counting,
/// security stamps); this class only reads and writes rows. Every update checks the concurrency
/// stamp, so two administrators cannot silently overwrite each other (HTTP 412 on screen 17).
/// Authenticator keys are encrypted with Data Protection; recovery codes are stored as PBKDF2 hashes
/// (Identity's password hasher), never as the codes.
/// </summary>
internal sealed partial class UserStore(DbSession db, IPasswordHasher<GoldmarkUser> hasher, IDataProtectionProvider protection, TimeProvider time,
    ILogger<UserStore> logger) :
    IUserPasswordStore<GoldmarkUser>,
    IUserSecurityStampStore<GoldmarkUser>,
    IUserLockoutStore<GoldmarkUser>,
    IUserTwoFactorStore<GoldmarkUser>,
    IUserAuthenticatorKeyStore<GoldmarkUser>,
    IUserTwoFactorRecoveryCodeStore<GoldmarkUser>,
    IUserRoleStore<GoldmarkUser>
{
    public const string AuthenticatorKeyPurpose = "Goldmark.AuthenticatorKey.v1";

    private const string Columns = """
        Id, UserName, NormalizedUserName, DisplayName, PasswordHash, SecurityStamp, ConcurrencyStamp, LockoutEnd,
        AccessFailedCount, AuthenticatorKey, AuthenticatorSetUpAt, LastTotpTimeStep, IsDisabled, CreatedAt, CreatedBy, LastSignInAt
        """;

    private const string FindByIdSql = "SELECT " + Columns + " FROM cmdb.UserAccount WHERE Id = @id;";
    private const string FindByNameSql = "SELECT " + Columns + " FROM cmdb.UserAccount WHERE NormalizedUserName = @name;";

    private readonly IDataProtector _keys = protection.CreateProtector(AuthenticatorKeyPurpose);

    public void Dispose()
    {
        // DbSession belongs to the request scope.
    }

    // --- IUserStore -----------------------------------------------------------------------

    public async Task<IdentityResult> CreateAsync(GoldmarkUser user, CancellationToken cancellationToken)
    {
        user.ConcurrencyStamp = NewStamp();
        user.CreatedAt = time.GetUtcNow().UtcDateTime;
        await db.ExecuteAsync("""
            INSERT INTO cmdb.UserAccount (Id, UserName, NormalizedUserName, DisplayName, PasswordHash, SecurityStamp,
                ConcurrencyStamp, LockoutEnd, AccessFailedCount, AuthenticatorKey, AuthenticatorSetUpAt, LastTotpTimeStep,
                IsDisabled, CreatedAt, CreatedBy, LastSignInAt)
            VALUES (@Id, @UserName, @NormalizedUserName, @DisplayName, @PasswordHash, @SecurityStamp,
                @ConcurrencyStamp, @LockoutEnd, @AccessFailedCount, @AuthenticatorKey, @AuthenticatorSetUpAt, @LastTotpTimeStep,
                @IsDisabled, @CreatedAt, @CreatedBy, @LastSignInAt);
            """, user, cancellationToken);
        return IdentityResult.Success;
    }

    /// <summary>
    /// Writes every column Identity changes, if the row still has the concurrency stamp the user was
    /// read with. <c>LastTotpTimeStep</c> and <c>LastSignInAt</c> have their own atomic statements.
    /// </summary>
    public async Task<IdentityResult> UpdateAsync(GoldmarkUser user, CancellationToken cancellationToken)
    {
        var next = NewStamp();
        var rows = await db.ExecuteAsync("""
            UPDATE cmdb.UserAccount
            SET UserName = @UserName, NormalizedUserName = @NormalizedUserName, DisplayName = @DisplayName,
                PasswordHash = @PasswordHash, SecurityStamp = @SecurityStamp, LockoutEnd = @LockoutEnd,
                AccessFailedCount = @AccessFailedCount, AuthenticatorKey = @AuthenticatorKey,
                AuthenticatorSetUpAt = @AuthenticatorSetUpAt, IsDisabled = @IsDisabled, ConcurrencyStamp = @Next
            WHERE Id = @Id AND ConcurrencyStamp = @ConcurrencyStamp;
            """,
            new
            {
                user.Id,
                user.UserName,
                user.NormalizedUserName,
                user.DisplayName,
                user.PasswordHash,
                user.SecurityStamp,
                user.LockoutEnd,
                user.AccessFailedCount,
                user.AuthenticatorKey,
                user.AuthenticatorSetUpAt,
                user.IsDisabled,
                user.ConcurrencyStamp,
                Next = next,
            },
            cancellationToken);
        if (rows != 1)
        {
            return IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure());
        }

        user.ConcurrencyStamp = next;
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> DeleteAsync(GoldmarkUser user, CancellationToken cancellationToken)
    {
        var rows = await db.ExecuteAsync("DELETE FROM cmdb.UserAccount WHERE Id = @Id AND ConcurrencyStamp = @ConcurrencyStamp;",
            new { user.Id, user.ConcurrencyStamp }, cancellationToken);
        return rows == 1 ? IdentityResult.Success : IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure());
    }

    public Task<GoldmarkUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
        Guid.TryParse(userId, out var id)
            ? db.QuerySingleOrDefaultAsync<GoldmarkUser>(FindByIdSql, new { id }, cancellationToken)
            : Task.FromResult<GoldmarkUser?>(null);

    public Task<GoldmarkUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
        db.QuerySingleOrDefaultAsync<GoldmarkUser>(FindByNameSql, new { name = normalizedUserName }, cancellationToken);

    public Task<string> GetUserIdAsync(GoldmarkUser user, CancellationToken cancellationToken) => Task.FromResult(user.Id.ToString("D"));

    public Task<string?> GetUserNameAsync(GoldmarkUser user, CancellationToken cancellationToken) => Task.FromResult<string?>(user.UserName);

    public Task SetUserNameAsync(GoldmarkUser user, string? userName, CancellationToken cancellationToken)
    {
        user.UserName = userName ?? "";
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(GoldmarkUser user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.NormalizedUserName);

    public Task SetNormalizedUserNameAsync(GoldmarkUser user, string? normalizedName, CancellationToken cancellationToken)
    {
        user.NormalizedUserName = normalizedName ?? "";
        return Task.CompletedTask;
    }

    // --- Passwords, security stamp ------------------------------------------------------------

    public Task SetPasswordHashAsync(GoldmarkUser user, string? passwordHash, CancellationToken cancellationToken)
    {
        user.PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(GoldmarkUser user, CancellationToken cancellationToken) => Task.FromResult(user.PasswordHash);

    public Task<bool> HasPasswordAsync(GoldmarkUser user, CancellationToken cancellationToken) => Task.FromResult(user.PasswordHash is not null);

    public Task SetSecurityStampAsync(GoldmarkUser user, string stamp, CancellationToken cancellationToken)
    {
        user.SecurityStamp = stamp;
        return Task.CompletedTask;
    }

    public Task<string?> GetSecurityStampAsync(GoldmarkUser user, CancellationToken cancellationToken) => Task.FromResult<string?>(user.SecurityStamp);

    // --- Lockout (always enabled) ---------------------------------------------------------------

    public Task<DateTimeOffset?> GetLockoutEndDateAsync(GoldmarkUser user, CancellationToken cancellationToken) => Task.FromResult(user.LockoutEnd);

    public Task SetLockoutEndDateAsync(GoldmarkUser user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
    {
        user.LockoutEnd = lockoutEnd;
        return Task.CompletedTask;
    }

    public Task<int> IncrementAccessFailedCountAsync(GoldmarkUser user, CancellationToken cancellationToken) => Task.FromResult(++user.AccessFailedCount);

    public Task ResetAccessFailedCountAsync(GoldmarkUser user, CancellationToken cancellationToken)
    {
        user.AccessFailedCount = 0;
        return Task.CompletedTask;
    }

    public Task<int> GetAccessFailedCountAsync(GoldmarkUser user, CancellationToken cancellationToken) => Task.FromResult(user.AccessFailedCount);

    /// <summary>
    /// Counts a failed attempt in one statement, so concurrent failures are each counted; at
    /// <paramref name="threshold"/> the account is locked until <paramref name="lockoutEnd"/> (or
    /// later, if already locked longer) and the count starts again, as Identity does. A new
    /// concurrency stamp makes a write from a copy read before this failure fail rather than put the
    /// old count back. True if this failure locked the account (not if it was locked longer already).
    /// </summary>
    public async Task<bool> RecordAccessFailedAsync(GoldmarkUser user, int threshold, DateTimeOffset lockoutEnd, CancellationToken cancellationToken)
    {
        // SET reads the row's values from before the update, in every expression. A lockout is never
        // moved earlier: an administrator's lock (LockoutEnd = max) made since the row was read stands.
        var row = await db.QuerySingleOrDefaultAsync<(int Before, int AccessFailedCount, DateTimeOffset? LockoutEnd, string? ConcurrencyStamp)>("""
            UPDATE cmdb.UserAccount
            SET AccessFailedCount = CASE WHEN AccessFailedCount + 1 >= @threshold THEN 0 ELSE AccessFailedCount + 1 END,
                LockoutEnd = CASE WHEN AccessFailedCount + 1 < @threshold THEN LockoutEnd
                    WHEN LockoutEnd > @lockoutEnd THEN LockoutEnd ELSE @lockoutEnd END,
                ConcurrencyStamp = @next
            OUTPUT deleted.AccessFailedCount, inserted.AccessFailedCount, inserted.LockoutEnd, inserted.ConcurrencyStamp
            WHERE Id = @id;
            """, new { id = user.Id, threshold, lockoutEnd, next = NewStamp() }, cancellationToken);
        if (row.ConcurrencyStamp is not { } stamp)
        {
            throw new InvalidOperationException("The account no longer exists.");
        }

        user.AccessFailedCount = row.AccessFailedCount;
        user.LockoutEnd = row.LockoutEnd;
        user.ConcurrencyStamp = stamp;
        // This failure locked the account only if it set the end: not if a longer lock was already there.
        return row.Before + 1 >= threshold && row.LockoutEnd == lockoutEnd;
    }

    /// <summary>
    /// Clears the failed-attempt count after a correct answer, in one statement that cannot fail on a
    /// stamp a concurrent failure changed. The lockout end is left alone: a concurrent failure may
    /// just have locked the account.
    /// </summary>
    public async Task ClearAccessFailedAsync(GoldmarkUser user, CancellationToken cancellationToken)
    {
        await db.ExecuteAsync("UPDATE cmdb.UserAccount SET AccessFailedCount = 0 WHERE Id = @id AND AccessFailedCount <> 0;",
            new { id = user.Id }, cancellationToken);
        user.AccessFailedCount = 0;
    }

    public Task<bool> GetLockoutEnabledAsync(GoldmarkUser user, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task SetLockoutEnabledAsync(GoldmarkUser user, bool enabled, CancellationToken cancellationToken) =>
        enabled ? Task.CompletedTask : throw new NotSupportedException("Lockout cannot be switched off.");

    // --- Two-factor: mandatory, so enabled exactly when an authenticator is set up ---------------

    public Task<bool> GetTwoFactorEnabledAsync(GoldmarkUser user, CancellationToken cancellationToken) => Task.FromResult(user.HasAuthenticator);

    public Task SetTwoFactorEnabledAsync(GoldmarkUser user, bool enabled, CancellationToken cancellationToken) =>
        enabled == user.HasAuthenticator ? Task.CompletedTask : throw new NotSupportedException("Two-factor follows the authenticator key.");

    /// <summary>Stores the key encrypted; null removes it (two-factor reset).</summary>
    public Task SetAuthenticatorKeyAsync(GoldmarkUser user, string? key, CancellationToken cancellationToken)
    {
        user.AuthenticatorKey = key is null ? null : _keys.Protect(key);
        user.AuthenticatorSetUpAt = key is null ? null : time.GetUtcNow().UtcDateTime;
        return Task.CompletedTask;
    }

    /// <summary>
    /// The decrypted key; null if there is none, or if the key ring no longer holds the key it was
    /// encrypted with (DEC-038 item 5: the ring was lost or replaced). A code then fails like a wrong
    /// one, and a recovery code still signs the person in to set up a new authenticator.
    /// </summary>
    public Task<string?> GetAuthenticatorKeyAsync(GoldmarkUser user, CancellationToken cancellationToken)
    {
        if (user.AuthenticatorKey is null)
        {
            return Task.FromResult<string?>(null);
        }

        try
        {
            return Task.FromResult<string?>(_keys.Unprotect(user.AuthenticatorKey));
        }
        catch (CryptographicException)
        {
            LogAuthenticatorKeyUnreadable(logger, user.Id);
            return Task.FromResult<string?>(null);
        }
    }

    /// <summary>
    /// Records <paramref name="timeStep"/> as used if it is newer than the last accepted step;
    /// false means the code was already used (or an older one), so it must be refused.
    /// </summary>
    public async Task<bool> TryUseTimeStepAsync(GoldmarkUser user, long timeStep, CancellationToken cancellationToken)
    {
        var rows = await db.ExecuteAsync("""
            UPDATE cmdb.UserAccount SET LastTotpTimeStep = @timeStep
            WHERE Id = @id AND (LastTotpTimeStep IS NULL OR LastTotpTimeStep < @timeStep);
            """, new { id = user.Id, timeStep }, cancellationToken);
        if (rows == 1)
        {
            user.LastTotpTimeStep = timeStep;
        }

        return rows == 1;
    }

    /// <summary>
    /// Sets the last accepted step outright, for a new authenticator key: steps used with the old key
    /// say nothing about the new one, and the code that just proved the new key cannot be used again.
    /// </summary>
    public async Task ResetTimeStepAsync(GoldmarkUser user, long timeStep, CancellationToken cancellationToken)
    {
        await db.ExecuteAsync("UPDATE cmdb.UserAccount SET LastTotpTimeStep = @timeStep WHERE Id = @id;",
            new { id = user.Id, timeStep }, cancellationToken);
        user.LastTotpTimeStep = timeStep;
    }

    public async Task RecordSignInAsync(GoldmarkUser user, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        await db.ExecuteAsync("UPDATE cmdb.UserAccount SET LastSignInAt = @now WHERE Id = @id;", new { id = user.Id, now }, cancellationToken);
        user.LastSignInAt = now;
    }

    /// <summary>
    /// Writes the setup-complete row (first-run setup can never run again; the runtime login cannot
    /// remove it). A second attempt fails on the primary key, so only one setup ever completes.
    /// </summary>
    public Task MarkSetupCompleteAsync(GoldmarkUser administrator, CancellationToken cancellationToken) =>
        db.ExecuteAsync("INSERT INTO cmdb.Installation (Id, SetupCompletedBy) VALUES (1, @id);", new { id = administrator.Id }, cancellationToken);

    // --- Recovery codes -----------------------------------------------------------------------

    public async Task ReplaceCodesAsync(GoldmarkUser user, IEnumerable<string> recoveryCodes, CancellationToken cancellationToken)
    {
        await db.ExecuteAsync("DELETE FROM cmdb.RecoveryCode WHERE UserId = @id;", new { id = user.Id }, cancellationToken);
        foreach (var code in recoveryCodes)
        {
            await db.ExecuteAsync("INSERT INTO cmdb.RecoveryCode (UserId, CodeHash) VALUES (@id, @hash);",
                new { id = user.Id, hash = hasher.HashPassword(user, NormalizeRecoveryCode(code) ?? code) }, cancellationToken);
        }
    }

    /// <summary>Marks the matching unused code as used; each code works once.</summary>
    public async Task<bool> RedeemCodeAsync(GoldmarkUser user, string code, CancellationToken cancellationToken)
    {
        if (NormalizeRecoveryCode(code) is not { } normalized)
        {
            return false;
        }

        var unused = await db.QueryAsync<(long Id, string CodeHash)>(
            "SELECT Id, CodeHash FROM cmdb.RecoveryCode WHERE UserId = @id AND UsedAt IS NULL;", new { id = user.Id }, cancellationToken);
        foreach (var candidate in unused)
        {
            if (hasher.VerifyHashedPassword(user, candidate.CodeHash, normalized) != PasswordVerificationResult.Failed)
            {
                return await db.ExecuteAsync("UPDATE cmdb.RecoveryCode SET UsedAt = @now WHERE Id = @codeId AND UsedAt IS NULL;",
                    new { codeId = candidate.Id, now = time.GetUtcNow().UtcDateTime }, cancellationToken) == 1;
            }
        }

        return false;
    }

    public Task<int> CountCodesAsync(GoldmarkUser user, CancellationToken cancellationToken) =>
        db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM cmdb.RecoveryCode WHERE UserId = @id AND UsedAt IS NULL;",
            new { id = user.Id }, cancellationToken);

    /// <summary>
    /// A recovery code as Identity generates it (<c>XXXXX-XXXXX</c>), upper case, with or without the
    /// hyphen and spaces; null if it cannot be one.
    /// </summary>
    public static string? NormalizeRecoveryCode(string? code)
    {
        if (code is null || code.Length > 32)
        {
            return null;
        }

        var compact = string.Concat(code.Where(c => c is not (' ' or '-'))).ToUpperInvariant();
        return compact.Length == 10 && compact.All(char.IsAsciiLetterOrDigit) ? $"{compact[..5]}-{compact[5..]}" : null;
    }

    // --- Roles --------------------------------------------------------------------------------

    public async Task AddToRoleAsync(GoldmarkUser user, string roleName, CancellationToken cancellationToken) =>
        await db.ExecuteAsync("INSERT INTO cmdb.UserRole (UserId, Role) VALUES (@id, @role);",
            new { id = user.Id, role = PersonRoleOrThrow(roleName) }, cancellationToken);

    public async Task RemoveFromRoleAsync(GoldmarkUser user, string roleName, CancellationToken cancellationToken) =>
        await db.ExecuteAsync("DELETE FROM cmdb.UserRole WHERE UserId = @id AND Role = @role;",
            new { id = user.Id, role = PersonRoleOrThrow(roleName) }, cancellationToken);

    public async Task<IList<string>> GetRolesAsync(GoldmarkUser user, CancellationToken cancellationToken)
    {
        var roles = await db.QueryAsync<string>("SELECT Role FROM cmdb.UserRole WHERE UserId = @id;", new { id = user.Id }, cancellationToken);
        return [.. Roles.ForPeople.Where(r => roles.Contains(r, StringComparer.Ordinal))];
    }

    public async Task<bool> IsInRoleAsync(GoldmarkUser user, string roleName, CancellationToken cancellationToken) =>
        Roles.PersonRole(roleName) is { } role && await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM cmdb.UserRole WHERE UserId = @id AND Role = @role;", new { id = user.Id, role }, cancellationToken) == 1;

    public async Task<IList<GoldmarkUser>> GetUsersInRoleAsync(string roleName, CancellationToken cancellationToken) =>
        Roles.PersonRole(roleName) is { } role
            ? [.. await db.QueryAsync<GoldmarkUser>(
                "SELECT " + Columns + " FROM cmdb.UserAccount WHERE Id IN (SELECT UserId FROM cmdb.UserRole WHERE Role = @role);",
                new { role }, cancellationToken)]
            : [];

    /// <summary>Identity passes normalized (upper-case) names; Collector is never a person's role.</summary>
    private static string PersonRoleOrThrow(string roleName) =>
        Roles.PersonRole(roleName) ?? throw new InvalidOperationException("Not a role a person can hold.");

    private static string NewStamp() => Guid.NewGuid().ToString("N");

    [LoggerMessage(EventId = 3003, EventName = "AuthenticatorKeyUnreadable", Level = LogLevel.Warning,
        Message = "The authenticator key of user {UserId} cannot be decrypted: the Data Protection key ring no longer holds its key " +
            "(lost, or restored from an older backup). The user can sign in with a recovery code and set up a new authenticator.")]
    private static partial void LogAuthenticatorKeyUnreadable(ILogger logger, Guid userId);
}

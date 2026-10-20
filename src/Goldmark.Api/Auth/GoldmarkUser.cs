namespace Goldmark.Api.Auth;

/// <summary>
/// A row of <c>cmdb.UserAccount</c> (migration 0003), as ASP.NET Core Identity's user type.
/// <see cref="AuthenticatorKey"/> holds the Data Protection cipher text, never the key itself.
/// Times are UTC.
/// </summary>
internal sealed class GoldmarkUser
{
    public Guid Id { get; set; }

    public string UserName { get; set; } = "";

    public string NormalizedUserName { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string? PasswordHash { get; set; }

    public string SecurityStamp { get; set; } = "";

    public string ConcurrencyStamp { get; set; } = "";

    public DateTimeOffset? LockoutEnd { get; set; }

    public int AccessFailedCount { get; set; }

    public string? AuthenticatorKey { get; set; }

    public DateTime? AuthenticatorSetUpAt { get; set; }

    public long? LastTotpTimeStep { get; set; }

    public bool IsDisabled { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? LastSignInAt { get; set; }

    /// <summary>An invited person who has not redeemed the invitation yet.</summary>
    public bool IsPending => PasswordHash is null;

    public bool HasAuthenticator => AuthenticatorKey is not null;

    /// <summary>Locked by an administrator until unlocked, as opposed to an automatic lockout.</summary>
    public bool IsLockedByAdministrator => LockoutEnd == DateTimeOffset.MaxValue;

    public bool IsLockedOut(DateTimeOffset now) => LockoutEnd is { } end && end > now;
}

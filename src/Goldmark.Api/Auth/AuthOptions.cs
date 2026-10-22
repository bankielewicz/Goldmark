using Microsoft.Extensions.Options;

namespace Goldmark.Api.Auth;

/// <summary>
/// Sign-in, session and link settings (configuration section <c>Goldmark:Auth</c>), with the
/// defaults the owner chose (DEC-038 items 2 and 6; US-03).
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Goldmark:Auth";

    /// <summary>Failed sign-in attempts (password or code) that lock an account.</summary>
    public int LockoutThreshold { get; set; } = 5;

    public int LockoutMinutes { get; set; } = 15;

    /// <summary>A session ends after this long without a request.</summary>
    public int SessionIdleMinutes { get; set; } = 30;

    /// <summary>A session ends this long after sign-in, however active.</summary>
    public int SessionLifetimeHours { get; set; } = 12;

    /// <summary>How long an invitation or two-factor reset link works.</summary>
    public int LinkExpiryHours { get; set; } = 24;

    /// <summary>Wrong codes that revoke an invitation or reset link.</summary>
    public int LinkCodeAttempts { get; set; } = 5;

    /// <summary>Sign-in, setup and link requests allowed per client address per minute (HTTP 429 after).</summary>
    public int RequestsPerMinute { get; set; } = 30;

    /// <summary>
    /// The way back in when no administrator can sign in (DEC-042 item 6; <c>GOLDMARK_RECOVER_ADMIN</c>):
    /// the username of an enabled Admin. While set, each start logs a one-time password-and-two-factor
    /// link for them, which works for an hour with <see cref="RecoverAdminCode"/>. Empty: off, and a
    /// start ends any unused recovery link.
    /// </summary>
    public string RecoverAdmin { get; set; } = "";

    /// <summary>
    /// The recovery link's code (<c>GOLDMARK_RECOVER_CODE</c>), chosen by the operator and never logged,
    /// so reading the log alone is not enough (owner, DEC-042 item 6): 12 to 64 letters and digits.
    /// Required with <see cref="RecoverAdmin"/>.
    /// </summary>
    public string RecoverAdminCode { get; set; } = "";

    public TimeSpan Lockout => TimeSpan.FromMinutes(LockoutMinutes);

    public TimeSpan SessionIdle => TimeSpan.FromMinutes(SessionIdleMinutes);

    public TimeSpan SessionLifetime => TimeSpan.FromHours(SessionLifetimeHours);

    public TimeSpan LinkExpiry => TimeSpan.FromHours(LinkExpiryHours);
}

internal sealed class AuthOptionsValidator : IValidateOptions<AuthOptions>
{
    /// <summary><c>cmdb.UserAccount.UserName</c> is <c>nvarchar(64)</c> (migration 0003).</summary>
    private const int MaxUserNameLength = 64;

    public ValidateOptionsResult Validate(string? name, AuthOptions options)
    {
        var failures = new List<string>();
        Range(failures, nameof(AuthOptions.LockoutThreshold), options.LockoutThreshold, 1, 100);
        Range(failures, nameof(AuthOptions.LockoutMinutes), options.LockoutMinutes, 1, 1440);
        Range(failures, nameof(AuthOptions.SessionIdleMinutes), options.SessionIdleMinutes, 1, 1440);
        Range(failures, nameof(AuthOptions.SessionLifetimeHours), options.SessionLifetimeHours, 1, 168);
        Range(failures, nameof(AuthOptions.LinkExpiryHours), options.LinkExpiryHours, 1, 168);
        Range(failures, nameof(AuthOptions.LinkCodeAttempts), options.LinkCodeAttempts, 1, 20);
        Range(failures, nameof(AuthOptions.RequestsPerMinute), options.RequestsPerMinute, 1, 10000);
        if (options.RecoverAdmin is { Length: > 0 } recover && (string.IsNullOrWhiteSpace(recover) || recover.Trim().Length > MaxUserNameLength))
        {
            // Names the setting only: never print a configured value.
            failures.Add($"{AuthOptions.SectionName}:RecoverAdmin must be a username of at most {MaxUserNameLength} characters, or empty.");
        }

        if (!string.IsNullOrWhiteSpace(options.RecoverAdmin) && LinkSecrets.NormalizeRecoveryCode(options.RecoverAdminCode) is null)
        {
            failures.Add($"{AuthOptions.SectionName}:RecoverAdminCode is required with RecoverAdmin: {LinkSecrets.RecoveryCodeMinLength} to " +
                $"{LinkSecrets.RecoveryCodeMaxLength} letters and digits (spaces and hyphens are ignored).");
        }

        if (failures.Count == 0 && options.SessionIdle > options.SessionLifetime)
        {
            failures.Add($"{AuthOptions.SectionName}:SessionIdleMinutes must not exceed SessionLifetimeHours.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void Range(List<string> failures, string setting, int value, int min, int max)
    {
        if (value < min || value > max)
        {
            failures.Add($"{AuthOptions.SectionName}:{setting} must be between {min} and {max}.");
        }
    }
}

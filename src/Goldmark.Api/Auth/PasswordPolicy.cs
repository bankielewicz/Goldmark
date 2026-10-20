using System.Collections.Frozen;
using System.Reflection;
using Microsoft.AspNetCore.Identity;

namespace Goldmark.Api.Auth;

/// <summary>
/// Passwords (DEC-038 item 6, NIST SP 800-63B): 12 to 128 characters, no composition rules, and not
/// a commonly used password. Identity's own validator checks the minimum length (its composition
/// rules are switched off); this one adds the maximum and the bundled list.
/// </summary>
internal sealed class PasswordPolicy : IPasswordValidator<GoldmarkUser>
{
    public const int MinimumLength = 12;
    public const int MaximumLength = 128;

    public const string TooLongCode = "PasswordTooLong";
    public const string CommonCode = "PasswordCommon";

    public Task<IdentityResult> ValidateAsync(UserManager<GoldmarkUser> manager, GoldmarkUser user, string? password)
    {
        password ??= "";
        if (password.Length > MaximumLength)
        {
            return Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = TooLongCode,
                Description = $"Passwords must be at most {MaximumLength} characters.",
            }));
        }

        if (password.Length >= MinimumLength && CommonPasswords.Contains(password))
        {
            return Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = CommonCode,
                Description = "This password is too common. Choose another.",
            }));
        }

        return Task.FromResult(IdentityResult.Success);
    }
}

/// <summary>
/// The bundled list of common passwords of 12 to 128 characters (Security/Passwords/
/// common-passwords.txt; source, licence and derivation in common-passwords.LICENSE.txt, DEC-040),
/// matched without regard to case. Loaded once, on first use.
/// </summary>
internal static class CommonPasswords
{
    public const string ResourceName = "Goldmark.Passwords.common-passwords.txt";

    private static readonly Lazy<FrozenSet<string>> List = new(Load);

    public static int Count => List.Value.Count;

    public static bool Contains(string password) => List.Value.Contains(password.ToLowerInvariant());

    private static FrozenSet<string> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        using var reader = new StreamReader(stream);
        var entries = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0)
            {
                entries.Add(line.ToLowerInvariant());
            }
        }

        return entries.ToFrozenSet(StringComparer.Ordinal);
    }
}

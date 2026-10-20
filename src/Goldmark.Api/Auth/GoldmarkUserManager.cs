using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Goldmark.Api.Auth;

/// <summary>
/// Identity's user manager with the few operations Goldmark's enrolment flows need and Identity does
/// not expose (DEC-040): checking a password against the policy before an account has one, installing
/// an authenticator key that the person has already proved with a code, and clearing two-factor for an
/// administrator's reset. Each changes the security stamp, which ends the person's other sessions.
/// </summary>
internal sealed class GoldmarkUserManager(
    IUserStore<GoldmarkUser> store,
    IOptions<IdentityOptions> optionsAccessor,
    IPasswordHasher<GoldmarkUser> passwordHasher,
    IEnumerable<IUserValidator<GoldmarkUser>> userValidators,
    IEnumerable<IPasswordValidator<GoldmarkUser>> passwordValidators,
    ILookupNormalizer keyNormalizer,
    IdentityErrorDescriber errors,
    IServiceProvider services,
    ILogger<UserManager<GoldmarkUser>> logger)
    : UserManager<GoldmarkUser>(store, optionsAccessor, passwordHasher, userValidators, passwordValidators, keyNormalizer, errors,
        services, logger)
{
    private UserStore Rows => (UserStore)Store;

    /// <summary>The password policy's verdict (length, common list) without changing anything.</summary>
    public Task<IdentityResult> CheckPasswordPolicyAsync(GoldmarkUser user, string password) => ValidatePasswordAsync(user, password);

    /// <summary>
    /// Installs a password hash computed earlier in the same enrolment (the person chose the password
    /// one step before setting up the authenticator), and a new security stamp.
    /// </summary>
    public async Task<IdentityResult> SetPasswordHashAsync(GoldmarkUser user, string passwordHash)
    {
        await Rows.SetPasswordHashAsync(user, passwordHash, CancellationToken);
        return await UpdateSecurityStampAsync(user);
    }

    /// <summary>
    /// Installs an authenticator key whose code was verified at <paramref name="verifiedTimeStep"/>,
    /// which is recorded so that code cannot be used again to sign in.
    /// </summary>
    public async Task<IdentityResult> SetAuthenticatorAsync(GoldmarkUser user, string base32Key, long verifiedTimeStep)
    {
        await Rows.SetAuthenticatorKeyAsync(user, base32Key, CancellationToken);
        var result = await UpdateSecurityStampAsync(user);
        if (result.Succeeded)
        {
            await Rows.ResetTimeStepAsync(user, verifiedTimeStep, CancellationToken);
        }

        return result;
    }

    /// <summary>Removes the authenticator key and every recovery code (an administrator's two-factor reset).</summary>
    public async Task<IdentityResult> ClearTwoFactorAsync(GoldmarkUser user)
    {
        await Rows.SetAuthenticatorKeyAsync(user, null, CancellationToken);
        await Rows.ReplaceCodesAsync(user, [], CancellationToken);
        return await UpdateSecurityStampAsync(user);
    }

    /// <summary>
    /// Checks a 6-digit code against the person's authenticator: the current step or one either side,
    /// and never a step already used (owner, DEC-040).
    /// </summary>
    public async Task<bool> VerifyAuthenticatorCodeAsync(GoldmarkUser user, string code, DateTimeOffset now)
    {
        if (await GetAuthenticatorKeyAsync(user) is not { } key || Totp.Match(Base32.Decode(key), code, now) is not { } step)
        {
            return false;
        }

        return await Rows.TryUseTimeStepAsync(user, step, CancellationToken);
    }

    public Task RecordSignInAsync(GoldmarkUser user) => Rows.RecordSignInAsync(user, CancellationToken);

    public Task MarkSetupCompleteAsync(GoldmarkUser administrator) => Rows.MarkSetupCompleteAsync(administrator, CancellationToken);
}

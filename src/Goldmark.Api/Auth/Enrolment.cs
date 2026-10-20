using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace Goldmark.Api.Auth;

/// <summary>What an enrolment sets up: the first administrator, an invited person, two-factor again, or a new authenticator.</summary>
internal enum EnrolmentKind
{
    Setup,
    Invitation,
    TwoFactorReset,
    ChangeAuthenticator,
}

/// <summary>
/// The steps of an enrolment done so far, carried in the enrolment cookie (encrypted and signed by
/// Data Protection, 30 minutes) so nothing is written until the last step succeeds: setup creates the
/// administrator, and an invitation sets the password and authenticator, in one transaction. Holds a
/// password hash (never the password) and the new authenticator key until it is verified.
/// </summary>
internal sealed record EnrolmentState(
    EnrolmentKind Kind,
    Guid? UserId = null,
    Guid? LinkId = null,
    string? SecurityStamp = null,
    string? SetupTokenHash = null,
    string? UserName = null,
    string? DisplayName = null,
    string? PasswordHash = null,
    bool PasswordVerified = false,
    string? AuthenticatorKey = null)
{
    private const string Prefix = "goldmark:enrol:";

    /// <summary>The next step the SPA must show.</summary>
    public string NextStep => this switch
    {
        { Kind: EnrolmentKind.Setup, PasswordHash: null } => "account",
        { Kind: EnrolmentKind.Invitation, PasswordHash: null } => "password",
        { Kind: EnrolmentKind.TwoFactorReset, PasswordVerified: false } => "password",
        { AuthenticatorKey: null } => "authenticator",
        _ => "verify",
    };

    /// <summary>The password step is done (a new password chosen, or the current one verified).</summary>
    public bool HasPassword => PasswordHash is not null || PasswordVerified;

    public ClaimsPrincipal ToPrincipal()
    {
        var identity = new ClaimsIdentity(AuthSchemes.Enrolment);
        Add(identity, "kind", Kind.ToString());
        if (UserId is { } user)
        {
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.ToString("D")));
        }

        Add(identity, "link", LinkId?.ToString("D"));
        Add(identity, "stamp", SecurityStamp);
        Add(identity, "setup", SetupTokenHash);
        Add(identity, "user-name", UserName);
        Add(identity, "display-name", DisplayName);
        Add(identity, "password-hash", PasswordHash);
        Add(identity, "password-verified", PasswordVerified ? "true" : null);
        Add(identity, "key", AuthenticatorKey);
        return new ClaimsPrincipal(identity);
    }

    public static EnrolmentState? From(ClaimsPrincipal principal)
    {
        if (!Enum.TryParse<EnrolmentKind>(Get(principal, "kind"), out var kind))
        {
            return null;
        }

        return new EnrolmentState(
            kind,
            Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var user) ? user : null,
            Guid.TryParse(Get(principal, "link"), out var link) ? link : null,
            Get(principal, "stamp"),
            Get(principal, "setup"),
            Get(principal, "user-name"),
            Get(principal, "display-name"),
            Get(principal, "password-hash"),
            Get(principal, "password-verified") == "true",
            Get(principal, "key"));
    }

    /// <summary>Stores this state in the enrolment cookie (replacing the previous one).</summary>
    public Task SaveAsync(HttpContext context) =>
        context.SignInAsync(AuthSchemes.Enrolment, ToPrincipal(), new AuthenticationProperties { IsPersistent = false });

    public static Task EndAsync(HttpContext context) => context.SignOutAsync(AuthSchemes.Enrolment);

    private static void Add(ClaimsIdentity identity, string name, string? value)
    {
        if (value is not null)
        {
            identity.AddClaim(new Claim(Prefix + name, value));
        }
    }

    private static string? Get(ClaimsPrincipal principal, string name) => principal.FindFirstValue(Prefix + name);
}

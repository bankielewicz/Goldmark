namespace Goldmark.Api.Security;

/// <summary>
/// Declares, on every endpoint, which access policy applies and why. The fallback authorization
/// policy denies everything else (deny by default, US-04), and the M6 enumeration test
/// (<c>EndpointInventoryTests</c>) requires every endpoint to carry either an authorization policy
/// with this metadata (<see cref="EndpointAccessExtensions.RequireAccess{TBuilder}"/>) or this metadata
/// with <see cref="AccessPolicy.Anonymous"/> (<see cref="EndpointAccessExtensions.AllowAnonymousByDesign{TBuilder}"/>).
/// </summary>
public sealed record EndpointAccess(string Policy, string Rationale);

public static class AccessPolicy
{
    public const string Anonymous = "Anonymous";

    /// <summary>Signed in with password and second factor (any role).</summary>
    public const string SignedIn = "SignedIn";

    /// <summary>Signed in, with the Admin role.</summary>
    public const string Admin = "Admin";

    /// <summary>Signed in, with the Admin or the Auditor role (the audit log, US-04, US-07).</summary>
    public const string AuditReader = "AuditReader";

    /// <summary>Signed in, with the Admin or the Editor role: creates, changes, retires and restores CIs (US-11).</summary>
    public const string CiEditor = "CiEditor";

    /// <summary>Password verified, waiting for the second factor (sign-in step 2 only).</summary>
    public const string TwoFactorPending = "TwoFactorPending";

    /// <summary>Setup, an invitation, a two-factor reset or a new authenticator in progress.</summary>
    public const string Enrolment = "Enrolment";

    /// <summary>Every policy an endpoint may require, with what it means (docs/security/endpoint-access.md).</summary>
    public static readonly IReadOnlyDictionary<string, string> Policies = new Dictionary<string, string>
    {
        [SignedIn] = "Signed in with password and second factor; any role.",
        [Admin] = "Signed in, with the Admin role.",
        [AuditReader] = "Signed in, with the Admin or the Auditor role.",
        [CiEditor] = "Signed in, with the Admin or the Editor role.",
        [TwoFactorPending] = "Password verified within the last 5 minutes; second factor not yet given.",
        [Enrolment] = "An enrolment in progress (setup, invitation, two-factor reset or new authenticator), for 30 minutes.",
    };
}

public static class EndpointAccessExtensions
{
    /// <summary>Marks an endpoint as intentionally anonymous and records the reason.</summary>
    public static TBuilder AllowAnonymousByDesign<TBuilder>(this TBuilder builder, string rationale)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rationale);
        return builder.AllowAnonymous().WithMetadata(new EndpointAccess(AccessPolicy.Anonymous, rationale));
    }

    /// <summary>Requires an authorization policy and records it, in one call so the two cannot diverge.</summary>
    public static TBuilder RequireAccess<TBuilder>(this TBuilder builder, string policy)
        where TBuilder : IEndpointConventionBuilder
    {
        if (!AccessPolicy.Policies.TryGetValue(policy, out var meaning))
        {
            throw new ArgumentException($"Unknown access policy '{policy}'.", nameof(policy));
        }

        return builder.RequireAuthorization(policy).WithMetadata(new EndpointAccess(policy, meaning));
    }
}

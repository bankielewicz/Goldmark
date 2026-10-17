namespace Goldmark.Api.Security;

/// <summary>
/// Declares, on every endpoint, which access policy applies and why. Slice 1 has only
/// anonymous-by-design endpoints; slice 3 adds the deny-by-default fallback policy and the
/// enumeration test that requires every endpoint to carry either an authorization policy or
/// this metadata with <see cref="AccessPolicy.Anonymous"/> (PRD US-04, M6).
/// </summary>
public sealed record EndpointAccess(string Policy, string Rationale);

public static class AccessPolicy
{
    public const string Anonymous = "Anonymous";
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
}

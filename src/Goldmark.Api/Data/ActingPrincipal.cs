using Goldmark.Api.Auth;

namespace Goldmark.Api.Data;

/// <summary>
/// Who the current request acts as: the signed-in person, or nobody (an anonymous request, a job, a
/// command). One answer for the audit log's actor (<c>AuditTrail</c>) and for <c>ChangedBy</c>
/// (<see cref="DbSession"/> puts it in the connection's <c>SESSION_CONTEXT</c>; FR-CM-5, DEC-047), so the
/// two can't name different people. Slice 10's collectors add sources here.
/// </summary>
internal sealed class ActingPrincipal(IHttpContextAccessor http)
{
    /// <summary>The signed-in person's account ID, or null when the request has no session.</summary>
    public Guid? UserId =>
        http.HttpContext?.User is { } user && user.Identity?.AuthenticationType == AuthSchemes.Session ? user.UserId() : null;

    /// <summary>What <c>ChangedBy</c> holds for this principal: the account ID as text (DEC-044), or null for nobody.</summary>
    public string? ChangedBy => UserId?.ToString("D");
}

namespace Goldmark.Api.Auth;

/// <summary>The roles of US-04, as in <c>cmdb.Role</c>.</summary>
internal static class Roles
{
    public const string Admin = "Admin";
    public const string Editor = "Editor";
    public const string Viewer = "Viewer";
    public const string Auditor = "Auditor";

    /// <summary>Service identities only (API tokens, slices 8 and 13); never a person (DEC-038 item 10).</summary>
    public const string Collector = "Collector";

    /// <summary>Roles a person can hold, in display order.</summary>
    public static readonly IReadOnlyList<string> ForPeople = [Admin, Editor, Viewer, Auditor];

    /// <summary>Roles an invitation can grant: Admin is added after the first sign-in (DEC-038 item 2).</summary>
    public static readonly IReadOnlyList<string> ForInvitations = [Editor, Viewer, Auditor];

    /// <summary>The canonical name of a person's role, matched without regard to case; null if it is not one.</summary>
    public static string? PersonRole(string name) =>
        ForPeople.FirstOrDefault(r => string.Equals(r, name, StringComparison.OrdinalIgnoreCase));
}

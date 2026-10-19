namespace Goldmark.Api.Configuration;

/// <summary>
/// Settings used only by the one-shot <c>bootstrap-db</c> command (section <c>Goldmark:Bootstrap</c>).
/// The administrative credentials are given to the bootstrap container only, never to the
/// application container.
/// </summary>
public sealed class BootstrapOptions
{
    public const string SectionName = "Goldmark:Bootstrap";

    /// <summary><c>Create</c>: create the database and both logins as needed.</summary>
    public const string CreateMode = "Create";

    /// <summary><c>Existing</c>: create nothing; use the database and logins a DBA created (DEC-032).</summary>
    public const string ExistingMode = "Existing";

    /// <summary>
    /// Administrator login (CONTROL SERVER), used only by <c>bootstrap-db</c>. No default:
    /// <c>sa</c> is refused unless <see cref="AllowSaAdmin"/> is set (DEC-032).
    /// </summary>
    public string AdminUser { get; set; } = "";
    public string AdminPassword { get; set; } = "";

    /// <summary>
    /// Allows <c>sa</c> as <see cref="AdminUser"/>. Only the bundled SQL Server deployment sets it,
    /// until Goldmark generates its own administrator login (slice 2c).
    /// </summary>
    public bool AllowSaAdmin { get; set; }

    /// <summary><see cref="CreateMode"/> (default) or <see cref="ExistingMode"/>.</summary>
    public string Mode { get; set; } = CreateMode;

    /// <summary>
    /// Take over a runtime login or database user that exists but was not created by Goldmark
    /// (its password is set to the configured one and it is marked as managed). Off by default:
    /// such principals are refused and nothing is changed.
    /// </summary>
    public bool AdoptExistingLogin { get; set; }

    /// <summary>
    /// IDs of principal-audit findings about the shared instance that the operator accepts,
    /// separated by commas or white space (DEC-031). Findings about Goldmark's own principals
    /// cannot be accepted.
    /// </summary>
    public string AcceptedFindings { get; set; } = "";

    /// <summary>How long to wait for SQL Server to accept connections.</summary>
    public int WaitSeconds { get; set; } = 120;
}

namespace Goldmark.Api.Configuration;

/// <summary>
/// Settings used only by the one-shot <c>bootstrap-db</c> command (section <c>Goldmark:Bootstrap</c>).
/// The administrative credentials are given to the bootstrap container only, never to the
/// application container.
/// </summary>
public sealed class BootstrapOptions
{
    public const string SectionName = "Goldmark:Bootstrap";

    public string AdminUser { get; set; } = "sa";
    public string AdminPassword { get; set; } = "";

    /// <summary>How long to wait for SQL Server to accept connections.</summary>
    public int WaitSeconds { get; set; } = 120;
}

namespace Goldmark.Api.Configuration;

/// <summary>
/// The migration login (section <c>Goldmark:Migration</c>), used only by <c>bootstrap-db</c>,
/// which creates it, and by <c>migrate</c>, which applies the schema migrations with it. It is
/// <c>db_owner</c> of the Goldmark database and nothing else. Only the one-shot
/// <c>db-init</c> container receives it, never the application container.
/// </summary>
public sealed class MigrationOptions
{
    public const string SectionName = "Goldmark:Migration";

    public string User { get; set; } = "goldmark_migrator";
    public string Password { get; set; } = "";
}

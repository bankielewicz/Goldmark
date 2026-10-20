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

    /// <summary>No default: set it with <see cref="Password"/>, or leave both empty and let <c>bootstrap-db</c> generate them (<see cref="CredentialFile"/>).</summary>
    public string User { get; set; } = "";
    public string Password { get; set; } = "";

    /// <summary>Optional path of the migration login's credential file, which <c>bootstrap-db</c> writes and <c>migrate</c> reads.</summary>
    public string? CredentialFile { get; set; }
}

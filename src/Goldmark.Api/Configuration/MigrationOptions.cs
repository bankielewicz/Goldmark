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
    public const string DedicatedLogin = "Dedicated";
    public const string AdministratorLogin = "Administrator";

    /// <summary>
    /// <c>Dedicated</c> (default, Docker): the migration login below, <c>db_owner</c> only. <c>Administrator</c>
    /// (the Windows service, DEC-043 item 2): there is no migration login; <c>migrate</c> runs as the
    /// administrator <c>bootstrap-db</c> uses (the installing person's Windows account, or the SQL
    /// administrator login they type), which must be <c>db_owner</c> of the database or more, and no
    /// migration secret is stored. <see cref="User"/>, <see cref="Password"/> and
    /// <see cref="CredentialFile"/> stay empty.
    /// </summary>
    public string Login { get; set; } = DedicatedLogin;

    public bool UsesAdministrator => Login == AdministratorLogin;

    /// <summary>
    /// Both modes known, and no migration login under Windows authentication, where its password would be stored
    /// on the server (DEC-043 items 2 and 5). <c>bootstrap-db</c> checks the same; <c>migrate</c> and
    /// <c>rotate-db-credentials</c> also run on their own.
    /// </summary>
    public static IEnumerable<string> ModeErrors(DatabaseOptions db, MigrationOptions migration)
    {
        if (db.Authentication is not (DatabaseOptions.SqlAuthentication or DatabaseOptions.WindowsAuthentication))
        {
            yield return $"{DatabaseOptions.SectionName}:Authentication must be '{DatabaseOptions.SqlAuthentication}' or '{DatabaseOptions.WindowsAuthentication}'.";
        }

        if (migration.Login is not (DedicatedLogin or AdministratorLogin))
        {
            yield return $"{SectionName}:Login must be '{DedicatedLogin}' or '{AdministratorLogin}'.";
        }
        else if (db.UsesWindowsAuthentication && !migration.UsesAdministrator)
        {
            yield return $"{SectionName}:Login must be '{AdministratorLogin}' with {DatabaseOptions.SectionName}:Authentication " +
                $"'{DatabaseOptions.WindowsAuthentication}'.";
        }
    }

    /// <summary>No default: set it with <see cref="Password"/>, or leave both empty and let <c>bootstrap-db</c> generate them (<see cref="CredentialFile"/>).</summary>
    public string User { get; set; } = "";
    public string Password { get; set; } = "";

    /// <summary>Optional path of the migration login's credential file, which <c>bootstrap-db</c> writes and <c>migrate</c> reads.</summary>
    public string? CredentialFile { get; set; }
}

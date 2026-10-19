using System.Globalization;
using System.Text.RegularExpressions;

namespace Goldmark.Api.Data;

/// <summary>
/// The forward-only schema migrations embedded in this build (<c>db/migrations/NNNN_name.sql</c>,
/// DEC-022). The schema version is the number of the newest script: the version this build
/// expects is its newest embedded script, and the database's version is the newest script in
/// DbUp's journal (<c>dbo.SchemaVersions</c>).
/// </summary>
internal static partial class SchemaMigrations
{
    /// <summary>Logical name prefix of the embedded scripts (see Goldmark.Api.csproj).</summary>
    public const string ResourcePrefix = "Goldmark.Migrations.";

    public const string JournalSchema = "dbo";
    public const string JournalTable = "SchemaVersions";

    /// <summary>
    /// Applied script names, or no rows when the journal does not exist (or is not visible to
    /// the caller). Read-only; the runtime login holds only SELECT on the journal.
    /// </summary>
    public const string AppliedScriptsSql = """
        SET NOCOUNT ON;
        IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NOT NULL
            SELECT ScriptName FROM dbo.SchemaVersions;
        """;

    [GeneratedRegex(@"^Goldmark\.Migrations\.(\d{4})_[a-z0-9_]+\.sql$", RegexOptions.CultureInvariant)]
    private static partial Regex ScriptName();

    /// <summary>Embedded migration scripts, in the order DbUp applies them (ordinal by name).</summary>
    public static IReadOnlyList<string> Scripts { get; } =
        [.. typeof(SchemaMigrations).Assembly.GetManifestResourceNames().Where(IsMigrationResource).Order(StringComparer.Ordinal)];

    /// <summary>The schema version this build was made for.</summary>
    public static int ExpectedVersion { get; } = DatabaseVersion(Scripts);

    public static bool IsMigrationResource(string resourceName) =>
        resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal);

    /// <summary>The version number of a script name, or null if it is not a Goldmark migration name.</summary>
    public static int? VersionOf(string scriptName)
    {
        var match = ScriptName().Match(scriptName);
        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>The newest version among the given script names; 0 when there is none.</summary>
    public static int DatabaseVersion(IEnumerable<string> appliedScripts) =>
        appliedScripts.Select(VersionOf).Max() ?? 0;
}

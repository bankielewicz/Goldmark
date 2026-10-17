using System.Text.RegularExpressions;

namespace Goldmark.Api.Configuration;

/// <summary>
/// Allow-list for SQL Server identifiers that Goldmark creates (database and login names).
/// Values are additionally passed through QUOTENAME server-side; this check keeps names boring.
/// </summary>
internal static partial class SqlIdentifier
{
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool IsValid(string? value) => value is not null && Pattern().IsMatch(value);

    private static readonly HashSet<string> SystemDatabases =
        new(["master", "model", "msdb", "tempdb", "distribution", "mssqlsystemresource"], StringComparer.OrdinalIgnoreCase);

    /// <summary>Goldmark must get its own database, never a system database.</summary>
    public static bool IsSystemDatabase(string value) => SystemDatabases.Contains(value);
}

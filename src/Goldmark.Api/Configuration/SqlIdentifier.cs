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

    // Built-in database principals, and the role Goldmark's migrations create. A login with one
    // of these names cannot get a database user of its own name.
    private static readonly HashSet<string> ReservedPrincipals =
        new(["dbo", "guest", "sys", "INFORMATION_SCHEMA", "public", "goldmark_runtime"], StringComparer.OrdinalIgnoreCase);

    /// <summary>Names a Goldmark-managed login and database user must not take.</summary>
    public static bool IsReservedPrincipal(string value) =>
        ReservedPrincipals.Contains(value) || string.Equals(value, "sa", StringComparison.OrdinalIgnoreCase);
}

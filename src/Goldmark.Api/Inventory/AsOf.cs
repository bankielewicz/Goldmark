using System.Globalization;

namespace Goldmark.Api.Inventory;

/// <summary>
/// The <c>asOf</c> parameter of the inventory's reads (US-42, FR-IM-4; DEC-047): a past moment in UTC, at
/// which the CIs, their types and attributes, the relationships and their types are read as they were
/// (<c>FOR SYSTEM_TIME AS OF</c> on every table of the query). A read that doesn't take it refuses it, so
/// nothing answers a question about the past with today's data.
/// </summary>
internal static class AsOf
{
    public const string Parameter = "asOf";

    /// <summary>Opens a query that reads the past: the moment, as SQL Server's own type (see <see cref="CiStore"/> on DateTime parameters).</summary>
    public const string Declare = "DECLARE @at datetime2(7) = CONVERT(datetime2(7), @asOf, 126);\n";

    /// <summary>Follows a table's name in such a query.</summary>
    public const string Clause = " FOR SYSTEM_TIME AS OF @at";

    private static readonly string[] Formats =
    [
        "yyyy-MM-dd'T'HH:mm'Z'", "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
    ];

    /// <summary>
    /// The moment asked for, or null for "now". It must be UTC with a <c>Z</c> (a time without a zone would be
    /// read differently by each client) and not in the future. A version's own time, as the history gives it
    /// with seven decimals, shows that version.
    /// </summary>
    public static bool TryParse(IQueryCollection query, TimeProvider clock, out DateTime? moment, out string? error)
    {
        moment = null;
        error = null;
        var values = query[Parameter];
        if (values.Count == 0)
        {
            return true;
        }

        if (values.Count != 1 || !DateTime.TryParseExact(values[0], Formats, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            error = "InvalidTime";
            return false;
        }

        if (parsed > clock.GetUtcNow().UtcDateTime)
        {
            error = "FutureTime";
            return false;
        }

        moment = parsed;
        return true;
    }

    /// <summary>The moment as the queries take it (<see cref="Declare"/>).</summary>
    public static string? Stamp(DateTime? moment) => moment is { } m ? CiStore.Stamp(m) : null;

    /// <summary>The moment as the API writes it: UTC, with the seven decimals a version's time has.</summary>
    public static string Text(DateTime moment) => moment.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);
}

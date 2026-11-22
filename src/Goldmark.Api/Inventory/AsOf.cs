using System.Globalization;
using Goldmark.Api.Data;
using Goldmark.Api.Hosting;

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

    // Whether the moment is ahead of the database's clock. Every version's time is SQL Server's (the start of the
    // transaction that wrote it), so the future is judged there too: by the app's clock, a database a few seconds
    // ahead (DEC-030: it may be another server) would have "show the CI as of the change I just saved" refused.
    private const string IsFutureSql = "SELECT CAST(IIF(CONVERT(datetime2(7), @asOf, 126) > SYSUTCDATETIME(), 1, 0) AS bit);";

    /// <summary>
    /// The moment asked for, or null for "now". It must be UTC with a <c>Z</c> (a time without a zone would be
    /// read differently by each client). A version's own time, as the history gives it with seven decimals,
    /// shows that version. Whether it is in the future is the database's to say (<see cref="ReadAsync"/>).
    /// </summary>
    public static bool TryParse(IQueryCollection query, out DateTime? moment, out string? error)
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

        moment = parsed;
        return true;
    }

    /// <summary>
    /// The moment the request asks for (null for now), or the answer that refuses it (400, field <c>asOf</c>):
    /// <c>InvalidTime</c>, or <c>FutureTime</c> for a moment ahead of the database's clock.
    /// </summary>
    public static async Task<(DateTime? Moment, IResult? Refusal)> ReadAsync(HttpContext context)
    {
        if (!TryParse(context.Request.Query, out var moment, out var error))
        {
            return (null, Refusal(error!));
        }

        if (moment is null)
        {
            return (null, null);
        }

        var db = context.RequestServices.GetRequiredService<DbSession>();
        return await db.ExecuteScalarAsync<bool>(IsFutureSql, new { asOf = Stamp(moment) }, context.RequestAborted)
            ? (null, Refusal("FutureTime"))
            : (moment, null);
    }

    /// <summary>For a read that has no past: null, or the refusal when the request names a moment.</summary>
    public static IResult? RefuseIfAsked(HttpContext context) => context.Request.Query.ContainsKey(Parameter) ? Refusal("NotSupported") : null;

    private static IResult Refusal(string error) =>
        CiEndpoints.Fields(new Dictionary<string, string[]>(StringComparer.Ordinal) { [Parameter] = [error] });

    /// <summary>
    /// A CI that exists now and didn't at the moment asked for, with when it was created (its first version), so the
    /// page can say "It was created on …, after the time you're viewing."
    /// </summary>
    public static IResult NotThen(DateTime createdAt) =>
        ApiProblems.Problem(StatusCodes.Status404NotFound, "not-found-then", "This CI didn't exist at that time.",
            new Dictionary<string, object?> { ["createdAt"] = createdAt });

    /// <summary>The answer for a CI the reads found nothing of: 404 <c>not-found-then</c> when it exists but didn't at <paramref name="asOf"/>, else null.</summary>
    public static async Task<IResult?> NotThenAsync(CiStore cis, Guid id, DateTime? asOf) =>
        asOf is not null && await cis.CreatedAtAsync(id) is { } createdAt ? NotThen(createdAt) : null;

    /// <summary>The moment as the queries take it (<see cref="Declare"/>).</summary>
    public static string? Stamp(DateTime? moment) => moment is { } m ? CiStore.Stamp(m) : null;

    /// <summary>The moment as the API writes it: UTC, with the seven decimals a version's time has.</summary>
    public static string Text(DateTime moment) => moment.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);
}

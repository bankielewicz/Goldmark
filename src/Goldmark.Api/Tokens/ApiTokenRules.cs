using System.Globalization;

namespace Goldmark.Api.Tokens;

/// <summary>
/// What an API token may do (DEC-048 item 4): read and write the inventory, nothing administrative. An endpoint names
/// the one scope that reaches it (<c>RequireAccess(policy, scope)</c>); a token never reaches an endpoint that names
/// none (users, types and attributes administration, the audit log, accounts and sign-in, the tokens themselves).
/// <c>ingest:write</c> arrives with sources (slice 13), since such a token is bound to one source.
/// </summary>
internal static class TokenScopes
{
    /// <summary>Read CIs, their types, search and history.</summary>
    public const string CisRead = "cis:read";

    /// <summary>Create, change, retire and restore CIs.</summary>
    public const string CisWrite = "cis:write";

    /// <summary>Read relationships and the graph.</summary>
    public const string RelationshipsRead = "relationships:read";

    /// <summary>Add, change and end relationships.</summary>
    public const string RelationshipsWrite = "relationships:write";

    /// <summary>Every scope, in the order the screen lists them and a token stores them.</summary>
    public static readonly IReadOnlyList<string> All = [CisRead, CisWrite, RelationshipsRead, RelationshipsWrite];

    public static bool IsKnown(string? scope) => scope is not null && All.Contains(scope, StringComparer.Ordinal);

    /// <summary>
    /// The scopes a new token asks for, each once, in <see cref="All"/>'s order; or the field error: <c>Required</c>
    /// for none, <c>UnknownScope</c> when one isn't a scope of this build (names are matched exactly).
    /// </summary>
    public static (IReadOnlyList<string> Scopes, string? Error) Parse(IEnumerable<string?>? requested)
    {
        var asked = requested?.ToList() ?? [];
        if (asked.Count == 0)
        {
            return ([], "Required");
        }

        return asked.All(IsKnown) ? ([.. All.Where(s => asked.Contains(s, StringComparer.Ordinal))], null) : ([], "UnknownScope");
    }
}

/// <summary>
/// When a new token expires (DEC-048 item 5; the slice 8 Notes board, item 5): <c>expiresInDays</c>, 30, 90 or 180 days
/// counted from the moment it is created, or <c>expiresOn</c>, a date <c>yyyy-MM-dd</c> from tomorrow to 365 days
/// ahead (UTC) on which the token works to the end of the day, so it expires at 00:00 UTC the day after. A request gives
/// exactly one of the two: every token expires (US-06).
/// </summary>
internal sealed record TokenExpiry(int? Days, DateOnly? On)
{
    public const int MaxDaysAhead = 365;

    public static readonly IReadOnlyList<int> Presets = [30, 90, 180];

    /// <summary>
    /// The expiry a request asks for, or its field errors: <c>expires</c> <c>Required</c> (neither) or <c>Ambiguous</c>
    /// (both), <c>expiresInDays</c> <c>InvalidDays</c>, <c>expiresOn</c> <c>InvalidDate</c>. Whether the date is in
    /// range depends on today's date, which is the database's (<see cref="RangeError"/>).
    /// </summary>
    public static (TokenExpiry? Expiry, Dictionary<string, string[]> Errors) Parse(int? expiresInDays, string? expiresOn)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var on = string.IsNullOrEmpty(expiresOn) ? null : expiresOn;
        if ((expiresInDays is null) == (on is null))
        {
            errors["expires"] = [on is null ? "Required" : "Ambiguous"];
            return (null, errors);
        }

        if (expiresInDays is { } days)
        {
            if (!Presets.Contains(days))
            {
                errors["expiresInDays"] = ["InvalidDays"];
                return (null, errors);
            }

            return (new TokenExpiry(days, null), errors);
        }

        if (!DateOnly.TryParseExact(on, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            errors["expiresOn"] = ["InvalidDate"];
            return (null, errors);
        }

        return (new TokenExpiry(null, date), errors);
    }

    /// <summary><c>OutOfRange</c> when a custom date isn't from tomorrow to 365 days after <paramref name="today"/> (UTC), else null.</summary>
    public string? RangeError(DateOnly today) =>
        On is { } date && (date <= today || date > today.AddDays(MaxDaysAhead)) ? "OutOfRange" : null;

    /// <summary>When a token created at <paramref name="createdAt"/> (UTC) expires.</summary>
    public DateTime ExpiresAt(DateTime createdAt) => On is { } date
        ? date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
        : DateTime.SpecifyKind(createdAt, DateTimeKind.Utc).AddDays(Days!.Value);
}

/// <summary>
/// A token's name (the slice 8 Notes board, item 6): required once trimmed, at most 100 characters, no control
/// characters, and unique among all tokens, revoked and expired ones too, ignoring case and surrounding spaces.
/// </summary>
internal static class ApiTokenNames
{
    /// <summary><c>cmdb.ApiToken.Name</c> and <c>NormalizedName</c> are <c>nvarchar(100)</c> (0010).</summary>
    public const int MaxLength = 100;

    /// <summary>The field error for the name: <c>Required</c>, <c>TooLong</c> or <c>InvalidText</c>; null when it may be used.</summary>
    public static string? Error(string? name) => name?.Trim() switch
    {
        null or "" => "Required",
        { } trimmed when trimmed.Length > MaxLength || Normalize(trimmed).Length > MaxLength => "TooLong",
        { } trimmed when trimmed.Any(char.IsControl) => "InvalidText",
        _ => null,
    };

    /// <summary>The form names are matched in, as Identity's lookup normalizer forms user names: trimmed, composed, upper case.</summary>
    public static string Normalize(string name) => name.Trim().Normalize().ToUpperInvariant();
}

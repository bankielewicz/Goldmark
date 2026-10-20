using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Goldmark.Api.Audit;

/// <summary>
/// The audit log's filters, from the query string (screen 16; US-07). Every value is validated before it
/// reaches SQL or the export's audit entry: times are ISO 8601, people are ids, actions come from
/// <see cref="AuditCatalog"/>, and an IP address prefix holds only hexadecimal digits, dots and colons,
/// so it can never carry a LIKE wildcard or free text.
/// </summary>
internal sealed partial record AuditQuery(
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? ActorType,
    Guid? ActorId,
    Guid? TargetId,
    IReadOnlyList<string> Actions,
    string? Outcome,
    string? IpPrefix,
    bool Oldest)
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 500;

    public static readonly AuditQuery None = new(null, null, null, null, null, [], null, null, false);

    /// <summary>The query, or a validation problem naming each bad parameter (never echoing its value).</summary>
    public static (AuditQuery? Query, IResult? Problem) Parse(IQueryCollection query)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var from = Time(query, "from", errors);
        var to = Time(query, "to", errors);
        if (from is not null && to is not null && from >= to)
        {
            errors["to"] = ["ToBeforeFrom"];
        }

        string? actorType = null;
        Guid? actorId = null;
        if (Single(query, "actor") is { } actor)
        {
            if (actor is AuditActorTypes.System or AuditActorTypes.Anonymous)
            {
                actorType = actor;
            }
            else if (Guid.TryParseExact(actor, "D", out var id))
            {
                (actorType, actorId) = (AuditActorTypes.User, id);
            }
            else
            {
                errors["actor"] = ["InvalidActor"];
            }
        }

        Guid? targetId = null;
        if (Single(query, "target") is { } target)
        {
            if (Guid.TryParseExact(target, "D", out var id))
            {
                targetId = id;
            }
            else
            {
                errors["target"] = ["InvalidTarget"];
            }
        }

        var actions = query["action"].Where(a => !string.IsNullOrEmpty(a)).Select(a => a!).Distinct(StringComparer.Ordinal).ToList();
        if (actions.Any(a => AuditCatalog.Find(a) is null))
        {
            errors["action"] = ["UnknownAction"];
        }

        var outcome = Single(query, "outcome");
        if (outcome is not null && !AuditCatalog.Outcomes.Contains(outcome, StringComparer.Ordinal))
        {
            errors["outcome"] = ["UnknownOutcome"];
        }

        var ip = Single(query, "ip");
        if (ip is not null && !IpPrefixPattern().IsMatch(ip))
        {
            errors["ip"] = ["InvalidIpPrefix"];
        }

        var order = Single(query, "order") ?? "newest";
        if (order is not ("newest" or "oldest"))
        {
            errors["order"] = ["InvalidOrder"];
        }

        if (errors.Count > 0)
        {
            return (null, Problem(errors));
        }

        return (new AuditQuery(from, to, actorType, actorId, targetId, actions, outcome, ip, order == "oldest"), null);
    }

    /// <summary>The page size and the cursor (the last entry number seen) for the list.</summary>
    public static (int Limit, long? Cursor, IResult? Problem) Page(IQueryCollection query)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var limit = DefaultLimit;
        if (Single(query, "limit") is { } text && (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > MaxLimit))
        {
            errors["limit"] = ["InvalidLimit"];
        }

        long? cursor = null;
        if (Single(query, "cursor") is { } after)
        {
            if (long.TryParse(after, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0)
            {
                cursor = id;
            }
            else
            {
                errors["cursor"] = ["InvalidCursor"];
            }
        }

        return errors.Count > 0 ? (0, null, Problem(errors)) : (limit, cursor, null);
    }

    /// <summary>
    /// The actions to match as a JSON array for OPENJSON, or null for any: the Action filter, narrowed to
    /// the chosen outcome. An empty array matches nothing.
    /// </summary>
    public string? ActionsJson()
    {
        IEnumerable<string>? codes = Actions.Count > 0 ? Actions : null;
        if (Outcome is not null)
        {
            var withOutcome = AuditCatalog.Actions.Where(a => a.Outcome == Outcome).Select(a => a.Code);
            codes = codes is null ? withOutcome : codes.Intersect(withOutcome, StringComparer.Ordinal);
        }

        return codes is null ? null : JsonSerializer.Serialize(codes.ToList());
    }

    /// <summary>The filters as the export's audit entry records them: only those that are set.</summary>
    public Dictionary<string, object> Describe()
    {
        var filters = new Dictionary<string, object>(StringComparer.Ordinal);
        if (From is { } from)
        {
            filters["from"] = from.UtcDateTime;
        }

        if (To is { } to)
        {
            filters["to"] = to.UtcDateTime;
        }

        if (ActorId is { } actorId)
        {
            filters["actor"] = actorId;
        }
        else if (ActorType is { } actorType)
        {
            filters["actor"] = actorType;
        }

        if (TargetId is { } targetId)
        {
            filters["target"] = targetId;
        }

        if (Actions.Count > 0)
        {
            filters["actions"] = Actions;
        }

        if (Outcome is { } outcome)
        {
            filters["outcome"] = outcome;
        }

        if (IpPrefix is { } ip)
        {
            filters["ip"] = ip;
        }

        filters["order"] = Oldest ? "oldest" : "newest";
        return filters;
    }

    private static string? Single(IQueryCollection query, string name) =>
        query.TryGetValue(name, out var values) && values.Count > 0 && !string.IsNullOrEmpty(values[^1]) ? values[^1] : null;

    private static DateTimeOffset? Time(IQueryCollection query, string name, Dictionary<string, string[]> errors)
    {
        if (Single(query, name) is not { } text)
        {
            return null;
        }

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time)
            && time.Year is >= 2000 and <= 9998)
        {
            return time;
        }

        errors[name] = ["InvalidTime"];
        return null;
    }

    private static IResult Problem(Dictionary<string, string[]> errors) =>
        Results.ValidationProblem(errors, title: "Some values are not valid.", extensions: new Dictionary<string, object?> { ["code"] = "validation" });

    [GeneratedRegex("^[0-9A-Fa-f.:]{1,45}$", RegexOptions.CultureInvariant)]
    private static partial Regex IpPrefixPattern();
}

/// <summary>The actor types <see cref="AuditActor"/> writes.</summary>
internal static class AuditActorTypes
{
    public const string User = "User";
    public const string System = "System";
    public const string Anonymous = "Anonymous";
}

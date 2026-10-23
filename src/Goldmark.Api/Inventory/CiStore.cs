using System.Globalization;
using System.Text;
using System.Text.Json;
using Goldmark.Api.Data;

namespace Goldmark.Api.Inventory;

/// <summary>A row of screen 4's list.</summary>
internal sealed record CiSummary(Guid Id, string Name, string Type, string LifecycleState, DateTime ChangedAt);

/// <summary>How many CIs of a type match the search, whatever types are ticked (screen 4's type filter).</summary>
internal sealed record TypeCount(string Type, int Count);

/// <summary>
/// One page of the search. <c>Total</c> and <c>Types</c> come with the first page only; <c>Next</c> is the
/// cursor of the page after, or null at the end.
/// </summary>
internal sealed record CiPage(IReadOnlyList<CiSummary> Items, long? Total, IReadOnlyList<TypeCount>? Types, string? Next);

/// <summary>A CI another one links to through an attribute, as its detail page shows it.</summary>
internal sealed record CiReference(Guid Id, string Name, string Type, string LifecycleState);

/// <summary>Who saved a CI last; <c>DisplayName</c> is null when the account no longer exists.</summary>
internal sealed record CiChangedBy(Guid? Id, string? DisplayName);

/// <summary>A CI as screen 5 shows it (US-11). <c>ETag</c> is the version If-Match must name to change it.</summary>
internal sealed record CiDetail(Guid Id, string Name, string Type, string LifecycleState, JsonElement Attributes,
    IReadOnlyDictionary<string, CiReference> References, DateTime ChangedAt, CiChangedBy? ChangedBy, string ETag);

/// <summary>
/// Screen 4's query (US-53 <c>/search?q=</c>; DEC-044 item 6): part of a name in any case, the ticked
/// types, retired CIs only when asked, one of six orders, and a cursor.
/// </summary>
internal sealed record CiSearch(string? Pattern, IReadOnlyList<int>? TypeIds, bool Retired, string Sort, int Take, CiCursor? After)
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 200;

    public static readonly IReadOnlyList<string> Sorts = ["name", "-name", "type", "-type", "changed", "-changed"];

    /// <summary>The query from the request, or the field errors (each parameter named once).</summary>
    public static (CiSearch? Search, Dictionary<string, string[]> Errors) Parse(IQueryCollection query, IReadOnlyList<CiTypeInfo> types)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var q = query["q"].ToString().Trim();
        if (q.Length > AttributeValues.MaxNameLength)
        {
            errors["q"] = ["TooLong"];
        }

        var typeIds = new List<int>();
        foreach (var name in query["type"])
        {
            if (CiTypeCatalog.Find(types, name) is { } type)
            {
                typeIds.Add(type.Id);
            }
            else
            {
                errors["type"] = ["UnknownType"];
            }
        }

        var retired = query["retired"].ToString() switch
        {
            "" or "false" => false,
            "true" => true,
            _ => (bool?)null,
        };
        if (retired is null)
        {
            errors["retired"] = ["InvalidBoolean"];
        }

        var sort = query["sort"].ToString() is { Length: > 0 } s ? s : "name";
        if (!Sorts.Contains(sort, StringComparer.Ordinal))
        {
            errors["sort"] = ["InvalidSort"];
        }

        var limit = DefaultLimit;
        if (query["limit"].ToString() is { Length: > 0 } l && (!int.TryParse(l, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > MaxLimit))
        {
            errors["limit"] = ["InvalidLimit"];
        }

        CiCursor? after = null;
        if (query["cursor"].ToString() is { Length: > 0 } c && (after = CiCursor.Decode(c, sort)) is null)
        {
            errors["cursor"] = ["InvalidCursor"];
        }

        return errors.Count > 0
            ? (null, errors)
            : (new CiSearch(q.Length == 0 ? null : "%" + EscapeLike(q) + "%", typeIds.Count == 0 ? null : typeIds, retired == true, sort, limit, after), errors);
    }

    /// <summary>LIKE's wildcards in the search text match themselves (the queries use <c>ESCAPE '\'</c>).</summary>
    public static string EscapeLike(string text)
    {
        var escaped = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch is '\\' or '%' or '_' or '[')
            {
                escaped.Append('\\');
            }

            escaped.Append(ch);
        }

        return escaped.ToString();
    }
}

/// <summary>
/// Where the next page starts: the sort it belongs to, the last row's sort keys and its ID, as an opaque
/// base64url JSON array. A cursor from another sort is refused.
/// </summary>
internal sealed record CiCursor(string Sort, string? Name, string? Type, string? Changed, Guid Id)
{
    public string Encode() =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new[] { Sort, Name, Type, Changed, Id.ToString("D") }))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static CiCursor? Decode(string value, string sort)
    {
        try
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            base64 += new string('=', (4 - (base64.Length % 4)) % 4);
            var parts = JsonSerializer.Deserialize<string?[]>(Convert.FromBase64String(base64));
            return parts is [{ } s, var name, var type, var changed, { } id] && s == sort && Guid.TryParseExact(id, "D", out var guid)
                && (changed is null || CiStore.TryParseStamp(changed, out _))
                ? new CiCursor(s, name, type, changed, guid)
                : null;
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Reads and writes <c>cmdb.Ci</c> (US-11; DEC-044). Names are matched and ordered in a case-insensitive
/// collation whatever the database's own (DEC-030: Goldmark may run on a DBA's server), so "any case"
/// holds everywhere. A CI's version is its <c>ValidFrom</c>, which every committed change moves (the
/// table is system-versioned): it travels as text and is compared as <c>datetime2(7)</c>, since a .NET
/// DateTime parameter would be sent as <c>datetime</c> and lose precision. <c>ChangedBy</c> holds the
/// person's account ID (slice 7's <c>SESSION_CONTEXT</c> writes the same).
/// </summary>
internal sealed class CiStore(DbSession db)
{
    public const string StampFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff";

    private const string Select = """
        SELECT TOP (@take) c.Id, c.Name, t.Name AS Type, c.LifecycleState, c.ValidFrom AS ChangedAt
        FROM cmdb.Ci AS c
        JOIN cmdb.CiType AS t ON t.Id = c.CiTypeId
        """;

    private const string Filters = """
         WHERE c.TenantId = @tenant
            AND (@pattern IS NULL OR c.Name COLLATE Latin1_General_100_CI_AS LIKE @pattern ESCAPE '\')
            AND (@retired = 1 OR c.LifecycleState <> 'Retired')
        """;

    private const string TypeFilter = """
         AND (@types IS NULL OR c.CiTypeId IN (SELECT CAST(value AS int) FROM OPENJSON(@types)))
        """;

    private const string ByName = Select + Filters + TypeFilter + """
         AND (@afterId IS NULL OR c.Name COLLATE Latin1_General_100_CI_AS > @afterName
            OR (c.Name COLLATE Latin1_General_100_CI_AS = @afterName AND c.Id > @afterId))
        ORDER BY c.Name COLLATE Latin1_General_100_CI_AS, c.Id;
        """;

    private const string ByNameDescending = Select + Filters + TypeFilter + """
         AND (@afterId IS NULL OR c.Name COLLATE Latin1_General_100_CI_AS < @afterName
            OR (c.Name COLLATE Latin1_General_100_CI_AS = @afterName AND c.Id < @afterId))
        ORDER BY c.Name COLLATE Latin1_General_100_CI_AS DESC, c.Id DESC;
        """;

    private const string ByType = Select + Filters + TypeFilter + """
         AND (@afterId IS NULL OR t.Name COLLATE Latin1_General_100_CI_AS > @afterType
            OR (t.Name COLLATE Latin1_General_100_CI_AS = @afterType AND (c.Name COLLATE Latin1_General_100_CI_AS > @afterName
                OR (c.Name COLLATE Latin1_General_100_CI_AS = @afterName AND c.Id > @afterId))))
        ORDER BY t.Name COLLATE Latin1_General_100_CI_AS, c.Name COLLATE Latin1_General_100_CI_AS, c.Id;
        """;

    private const string ByTypeDescending = Select + Filters + TypeFilter + """
         AND (@afterId IS NULL OR t.Name COLLATE Latin1_General_100_CI_AS < @afterType
            OR (t.Name COLLATE Latin1_General_100_CI_AS = @afterType AND (c.Name COLLATE Latin1_General_100_CI_AS < @afterName
                OR (c.Name COLLATE Latin1_General_100_CI_AS = @afterName AND c.Id < @afterId))))
        ORDER BY t.Name COLLATE Latin1_General_100_CI_AS DESC, c.Name COLLATE Latin1_General_100_CI_AS DESC, c.Id DESC;
        """;

    private const string ByChanged = Select + Filters + TypeFilter + """
         AND (@afterId IS NULL OR c.ValidFrom > CONVERT(datetime2(7), @afterChanged, 126)
            OR (c.ValidFrom = CONVERT(datetime2(7), @afterChanged, 126) AND c.Id > @afterId))
        ORDER BY c.ValidFrom, c.Id;
        """;

    private const string ByChangedDescending = Select + Filters + TypeFilter + """
         AND (@afterId IS NULL OR c.ValidFrom < CONVERT(datetime2(7), @afterChanged, 126)
            OR (c.ValidFrom = CONVERT(datetime2(7), @afterChanged, 126) AND c.Id < @afterId))
        ORDER BY c.ValidFrom DESC, c.Id DESC;
        """;

    private const string CountSql = "SELECT COUNT_BIG(*) FROM cmdb.Ci AS c" + Filters + TypeFilter + ";";

    // The ticked types don't narrow these counts, so a type that isn't ticked still shows how many it has.
    private const string TypeCountsSql = """
        SELECT t.Name AS Type, COUNT(*) AS Count
        FROM cmdb.Ci AS c
        JOIN cmdb.CiType AS t ON t.Id = c.CiTypeId
        """ + Filters + """
         GROUP BY t.Id, t.Name
        ORDER BY t.Id;
        """;

    private const string DetailColumns = """
        SELECT c.Id, c.Name, t.Name AS Type, c.CiTypeId, c.LifecycleState, CAST(c.Attributes AS nvarchar(max)) AS Attributes,
            c.ValidFrom, c.ChangedBy, u.DisplayName AS ChangedByName
        """;

    private const string DetailSql = DetailColumns + """

        FROM cmdb.Ci AS c
        JOIN cmdb.CiType AS t ON t.Id = c.CiTypeId
        LEFT JOIN cmdb.UserAccount AS u ON u.Id = TRY_CAST(c.ChangedBy AS uniqueidentifier)
        WHERE c.Id = @id AND c.TenantId = @tenant;
        """;

    // The same row, locked until the transaction ends, so the version checked is the version changed.
    private const string LockedDetailSql = DetailColumns + """

        FROM cmdb.Ci AS c WITH (UPDLOCK, HOLDLOCK)
        JOIN cmdb.CiType AS t ON t.Id = c.CiTypeId
        LEFT JOIN cmdb.UserAccount AS u ON u.Id = TRY_CAST(c.ChangedBy AS uniqueidentifier)
        WHERE c.Id = @id AND c.TenantId = @tenant;
        """;

    private const string ReferencesSql = """
        SELECT c.Id, c.Name, t.Name AS Type, c.LifecycleState, c.CiTypeId
        FROM cmdb.Ci AS c
        JOIN cmdb.CiType AS t ON t.Id = c.CiTypeId
        WHERE c.TenantId = @tenant AND c.Id IN (SELECT CAST(value AS uniqueidentifier) FROM OPENJSON(@ids));
        """;

    // A save checks the CIs it newly links to with a shared lock held to its commit, so a retire of one of them
    // (UPDLOCK on its own row, then an exclusive update) waits for the save, or the save sees it retired. Not
    // UPDLOCK: two update locks on one row are incompatible, and a save and a retire would deadlock.
    private const string LinkedForSaveSql = """
        SELECT c.Id, c.Name, t.Name AS Type, c.LifecycleState, c.CiTypeId
        FROM cmdb.Ci AS c WITH (HOLDLOCK)
        JOIN cmdb.CiType AS t ON t.Id = c.CiTypeId
        WHERE c.TenantId = @tenant AND c.Id IN (SELECT CAST(value AS uniqueidentifier) FROM OPENJSON(@ids));
        """;

    private const string InsertSql = """
        INSERT INTO cmdb.Ci (Id, TenantId, CiTypeId, Name, LifecycleState, Attributes, ChangedBy)
        VALUES (@id, @tenant, @typeId, @name, 'Active', @attributes, @changedBy);
        """;

    private const string UpdateSql = """
        UPDATE cmdb.Ci SET Name = @name, Attributes = @attributes, ChangedBy = @changedBy
        WHERE Id = @id AND TenantId = @tenant;
        """;

    private const string LifecycleSql = """
        UPDATE cmdb.Ci SET LifecycleState = @state, ChangedBy = @changedBy
        WHERE Id = @id AND TenantId = @tenant;
        """;

    public static string Stamp(DateTime validFrom) => validFrom.ToString(StampFormat, CultureInfo.InvariantCulture);

    public static bool TryParseStamp(string value, out DateTime stamp) =>
        DateTime.TryParseExact(value, StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out stamp);

    public async Task<CiPage> SearchAsync(CiSearch search)
    {
        var parameters = new
        {
            take = search.Take + 1,
            tenant = Tenants.Default,
            pattern = search.Pattern,
            retired = search.Retired,
            types = search.TypeIds is { } ids ? JsonSerializer.Serialize(ids) : null,
            afterId = search.After?.Id,
            afterName = search.After?.Name,
            afterType = search.After?.Type,
            afterChanged = search.After?.Changed,
        };
        var rows = search.Sort switch
        {
            "-name" => await db.QueryAsync<CiSummary>(ByNameDescending, parameters),
            "type" => await db.QueryAsync<CiSummary>(ByType, parameters),
            "-type" => await db.QueryAsync<CiSummary>(ByTypeDescending, parameters),
            "changed" => await db.QueryAsync<CiSummary>(ByChanged, parameters),
            "-changed" => await db.QueryAsync<CiSummary>(ByChangedDescending, parameters),
            _ => await db.QueryAsync<CiSummary>(ByName, parameters),
        };
        var items = rows.Take(search.Take).ToList();
        var next = rows.Count > search.Take && items[^1] is var last
            ? new CiCursor(search.Sort, last.Name, last.Type, Stamp(last.ChangedAt), last.Id).Encode()
            : null;
        if (search.After is not null)
        {
            return new CiPage(items, null, null, next);
        }

        var total = await db.ExecuteScalarAsync<long>(CountSql, parameters);
        var types = await db.QueryAsync<TypeCount>(TypeCountsSql, parameters);
        return new CiPage(items, total, types, next);
    }

    public Task<CiRow?> FindAsync(Guid id) => db.QuerySingleOrDefaultAsync<CiRow>(DetailSql, new { id, tenant = Tenants.Default });

    /// <summary>The CI, locked until the transaction ends (call inside one).</summary>
    public Task<CiRow?> FindForUpdateAsync(Guid id) => db.QuerySingleOrDefaultAsync<CiRow>(LockedDetailSql, new { id, tenant = Tenants.Default });

    /// <summary>The CIs with these IDs that exist, by ID.</summary>
    public async Task<IReadOnlyDictionary<Guid, LinkedCi>> LinkedAsync(IEnumerable<Guid> ids)
    {
        var list = ids.Distinct().Select(i => i.ToString("D")).ToList();
        return list.Count == 0
            ? new Dictionary<Guid, LinkedCi>()
            : (await db.QueryAsync<LinkedCi>(ReferencesSql, new { tenant = Tenants.Default, ids = JsonSerializer.Serialize(list) })).ToDictionary(r => r.Id);
    }

    /// <summary>The CIs a save newly links to, locked (shared) until the save's transaction ends.</summary>
    public async Task<IReadOnlyDictionary<Guid, LinkedCi>> LinkedForSaveAsync(IEnumerable<Guid> ids)
    {
        var list = ids.Distinct().Select(i => i.ToString("D")).ToList();
        return list.Count == 0
            ? new Dictionary<Guid, LinkedCi>()
            : (await db.QueryAsync<LinkedCi>(LinkedForSaveSql, new { tenant = Tenants.Default, ids = JsonSerializer.Serialize(list) })).ToDictionary(r => r.Id);
    }

    public async Task<CiDetail> DetailAsync(CiRow row, CiTypeInfo type)
    {
        var values = AttributeValues.Parse(row.Attributes);
        var linkIds = type.Attributes.Where(a => a.DataType == AttributeDataTypes.CiRef && values[a.Name] is not null)
            .Select(a => Guid.Parse(values[a.Name]!.GetValue<string>()));
        var linked = await LinkedAsync(linkIds);
        return new CiDetail(row.Id, row.Name, row.Type, row.LifecycleState, JsonSerializer.SerializeToElement(AttributeValues.ForResponse(type, values)),
            linked.Values.ToDictionary(l => l.Id.ToString("D"), l => new CiReference(l.Id, l.Name, l.Type, l.LifecycleState)),
            row.ValidFrom, Guid.TryParse(row.ChangedBy, out var by) ? new CiChangedBy(by, row.ChangedByName) : null, Stamp(row.ValidFrom));
    }

    public Task InsertAsync(Guid id, int typeId, string name, string attributes, Guid changedBy) =>
        db.ExecuteAsync(InsertSql, new { id, tenant = Tenants.Default, typeId, name, attributes, changedBy = changedBy.ToString("D") });

    public Task UpdateAsync(Guid id, string name, string attributes, Guid changedBy) =>
        db.ExecuteAsync(UpdateSql, new { id, tenant = Tenants.Default, name, attributes, changedBy = changedBy.ToString("D") });

    public Task SetLifecycleAsync(Guid id, string state, Guid changedBy) =>
        db.ExecuteAsync(LifecycleSql, new { id, tenant = Tenants.Default, state, changedBy = changedBy.ToString("D") });

    internal sealed record CiRow(Guid Id, string Name, string Type, int CiTypeId, string LifecycleState, string Attributes,
        DateTime ValidFrom, string ChangedBy, string? ChangedByName);

    internal sealed record LinkedCi(Guid Id, string Name, string Type, string LifecycleState, int CiTypeId);
}

/// <summary>The lifecycle states (US-12); slice 5 sets only Active and Retired (DEC-044 item 2).</summary>
internal static class LifecycleStates
{
    public const string Active = "Active";
    public const string Retired = "Retired";
}

using System.Text.Json;
using Goldmark.Api.Data;

namespace Goldmark.Api.Inventory;

/// <summary>A CI at one end of a relationship.</summary>
internal sealed record RelationshipEnd(Guid Id, string Name, string Type, string LifecycleState);

/// <summary>
/// A relationship as screen 5's Relationships tab and panel show it (DEC-046): <c>From</c> is the dependent, <c>To</c>
/// the dependency. <c>CreatedAt</c> is when it was added (item 13; "Since"), <c>ChangedAt</c> and <c>ChangedBy</c> its
/// last change, <c>ETag</c> the version If-Match must name to change or delete it.
/// </summary>
internal sealed record RelationshipDetail(Guid Id, string Type, RelationshipEnd From, RelationshipEnd To, string? RedundancyGroup,
    DateTime? CreatedAt, DateTime ChangedAt, CiChangedBy? ChangedBy, string ETag);

/// <summary>
/// One page of a section of the tab. <c>Total</c> comes with the first page only; <c>Next</c> is the cursor of the
/// page after, or null at the end.
/// </summary>
internal sealed record RelationshipPage(IReadOnlyList<RelationshipDetail> Items, long? Total, string? Next);

/// <summary>A redundancy group on a dependent CI's relationships, as the panel suggests it.</summary>
internal sealed record RedundancyGroupUse(string Name, int Relationships);

/// <summary>The tab's two sections: what a CI depends on (it is <c>From</c>) and what depends on it (it is <c>To</c>).</summary>
internal static class RelationshipDirections
{
    public const string Dependencies = "dependencies";
    public const string Dependents = "dependents";
}

/// <summary>
/// DEC-046 item 2's rule for redundancy group texts, in one place for every reader (the panel's suggestions, the
/// graph's group members and slice 19's impact engine): trimmed, an empty one is none, at most 200 characters and no
/// control characters; two texts are one group when they are equal ignoring case, ordinally, and are never
/// otherwise normalised. The texts are matched here, in .NET, not by a SQL collation, whose case-insensitive rules
/// also fold widths and some letter pairs.
/// </summary>
internal static class RedundancyGroups
{
    public const int MaxLength = 200;

    public static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>The text to store (null for none), or an error code.</summary>
    public static (string? Group, string? Error) Parse(string? value)
    {
        var group = value?.Trim();
        return group switch
        {
            null or "" => (null, null),
            { Length: > MaxLength } => (null, "TooLong"),
            _ when group.Any(char.IsControl) => (null, "InvalidText"),
            _ => (group, null),
        };
    }
}

/// <summary>
/// Where a section's next page starts: its direction, the last row's relationship type, the related CI's name and the
/// relationship's ID, as an opaque base64url JSON array. A cursor of the other section is refused.
/// </summary>
internal sealed record RelationshipCursor(string Direction, string Type, string Name, Guid Id)
{
    public string Encode() =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new[] { Direction, Type, Name, Id.ToString("D") }))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static RelationshipCursor? Decode(string value, string direction)
    {
        try
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            base64 += new string('=', (4 - (base64.Length % 4)) % 4);
            var parts = JsonSerializer.Deserialize<string?[]>(Convert.FromBase64String(base64));
            return parts is [{ } d, { } type, { } name, { } id] && d == direction && Guid.TryParseExact(id, "D", out var guid)
                ? new RelationshipCursor(d, type, name, guid)
                : null;
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Reads and writes <c>cmdb.Relationship</c> (FR-CM-5, FR-CM-6; DEC-046). The table is system-versioned: a change
/// moves the row's <c>ValidFrom</c>, its version (and ETag, compared as <c>datetime2(7)</c> as a CI's is), and a delete
/// leaves the last version in the history. <c>ChangedBy</c> holds the person's account ID, as for CIs. Each section of
/// the tab reads one index: what a CI depends on through <c>UQ_Relationship (FromCiId, …)</c>, what depends on it
/// through <c>IX_Relationship_ToCi</c>. Names are ordered in <c>Latin1_General_100_CI_AS</c>, as Search orders them.
/// </summary>
internal sealed class RelationshipStore(DbSession db)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    private const string Columns = """
        SELECT TOP (@take) r.Id, rt.Name AS Type, r.RedundancyGroup, r.CreatedAt, r.ValidFrom, r.ChangedBy, u.DisplayName AS ChangedByName,
            f.Id AS FromId, f.Name AS FromName, ft.Name AS FromType, f.LifecycleState AS FromState,
            t.Id AS ToId, t.Name AS ToName, tt.Name AS ToType, t.LifecycleState AS ToState
        """;

    private const string Joins = """

        JOIN cmdb.RelationshipType AS rt ON rt.Id = r.RelationshipTypeId
        JOIN cmdb.Ci AS f ON f.Id = r.FromCiId
        JOIN cmdb.CiType AS ft ON ft.Id = f.CiTypeId
        JOIN cmdb.Ci AS t ON t.Id = r.ToCiId
        JOIN cmdb.CiType AS tt ON tt.Id = t.CiTypeId
        LEFT JOIN cmdb.UserAccount AS u ON u.Id = TRY_CAST(r.ChangedBy AS uniqueidentifier)
        """;

    // A section is sorted by relationship type, then the related CI's name (screen 5's tab; Notes board).
    private const string DependenciesSql = Columns + " FROM cmdb.Relationship AS r" + Joins + """

        WHERE r.FromCiId = @id AND r.TenantId = @tenant
            AND (@afterId IS NULL OR rt.Name COLLATE Latin1_General_100_CI_AS > @afterType
                OR (rt.Name COLLATE Latin1_General_100_CI_AS = @afterType AND (t.Name COLLATE Latin1_General_100_CI_AS > @afterName
                    OR (t.Name COLLATE Latin1_General_100_CI_AS = @afterName AND r.Id > @afterId))))
        ORDER BY rt.Name COLLATE Latin1_General_100_CI_AS, t.Name COLLATE Latin1_General_100_CI_AS, r.Id;
        """;

    private const string DependentsSql = Columns + " FROM cmdb.Relationship AS r" + Joins + """

        WHERE r.ToCiId = @id AND r.TenantId = @tenant
            AND (@afterId IS NULL OR rt.Name COLLATE Latin1_General_100_CI_AS > @afterType
                OR (rt.Name COLLATE Latin1_General_100_CI_AS = @afterType AND (f.Name COLLATE Latin1_General_100_CI_AS > @afterName
                    OR (f.Name COLLATE Latin1_General_100_CI_AS = @afterName AND r.Id > @afterId))))
        ORDER BY rt.Name COLLATE Latin1_General_100_CI_AS, f.Name COLLATE Latin1_General_100_CI_AS, r.Id;
        """;

    private const string DependenciesCountSql = "SELECT COUNT_BIG(*) FROM cmdb.Relationship WHERE FromCiId = @id AND TenantId = @tenant;";

    private const string DependentsCountSql = "SELECT COUNT_BIG(*) FROM cmdb.Relationship WHERE ToCiId = @id AND TenantId = @tenant;";

    private const string DetailSql = Columns + " FROM cmdb.Relationship AS r" + Joins + """

        WHERE r.Id = @id AND r.TenantId = @tenant;
        """;

    // The same row, locked until the transaction ends, so the version checked is the version changed or deleted.
    private const string LockedDetailSql = Columns + " FROM cmdb.Relationship AS r WITH (UPDLOCK, HOLDLOCK)" + Joins + """

        WHERE r.Id = @id AND r.TenantId = @tenant;
        """;

    private const string GroupsSql = """
        SELECT RedundancyGroup FROM cmdb.Relationship
        WHERE FromCiId = @id AND TenantId = @tenant AND RedundancyGroup IS NOT NULL;
        """;

    // CreatedAt comes from its default (0008).
    private const string InsertSql = """
        INSERT INTO cmdb.Relationship (Id, TenantId, FromCiId, ToCiId, RelationshipTypeId, RedundancyGroup, ChangedBy)
        VALUES (@id, @tenant, @from, @to, @typeId, @group, @changedBy);
        """;

    private const string UpdateSql = """
        UPDATE cmdb.Relationship SET RedundancyGroup = @group, ChangedBy = @changedBy
        WHERE Id = @id AND TenantId = @tenant;
        """;

    // DEC-046 item 1: the row goes and its history keeps it as it was; who ended it is the audit entry's actor. Never
    // set ChangedBy first: that would leave a version that lasted no time.
    private const string DeleteSql = "DELETE FROM cmdb.Relationship WHERE Id = @id AND TenantId = @tenant;";

    /// <summary>One page of a section; the total with the first page.</summary>
    public async Task<RelationshipPage> SectionAsync(Guid ciId, string direction, int take, RelationshipCursor? after)
    {
        var dependencies = direction == RelationshipDirections.Dependencies;
        var parameters = new
        {
            take = take + 1,
            id = ciId,
            tenant = Tenants.Default,
            afterId = after?.Id,
            afterType = after?.Type,
            afterName = after?.Name,
        };
        var rows = dependencies ? await db.QueryAsync<Row>(DependenciesSql, parameters) : await db.QueryAsync<Row>(DependentsSql, parameters);
        var items = rows.Take(take).Select(ToDetail).ToList();
        var next = rows.Count > take && items[^1] is var last
            ? new RelationshipCursor(direction, last.Type, dependencies ? last.To.Name : last.From.Name, last.Id).Encode()
            : null;
        long? total = null;
        if (after is null)
        {
            var count = new { id = ciId, tenant = Tenants.Default };
            total = dependencies ? await db.ExecuteScalarAsync<long>(DependenciesCountSql, count) : await db.ExecuteScalarAsync<long>(DependentsCountSql, count);
        }

        return new RelationshipPage(items, total, next);
    }

    public async Task<RelationshipDetail?> FindAsync(Guid id) =>
        await db.QuerySingleOrDefaultAsync<Row>(DetailSql, Parameters(id)) is { } row ? ToDetail(row) : null;

    /// <summary>The relationship, locked until the transaction ends (call inside one).</summary>
    public async Task<RelationshipDetail?> FindForUpdateAsync(Guid id) =>
        await db.QuerySingleOrDefaultAsync<Row>(LockedDetailSql, Parameters(id)) is { } row ? ToDetail(row) : null;

    /// <summary>
    /// The redundancy groups on the CI's relationships as the dependent (DEC-046 item 2), each with how many use it,
    /// named by its most used spelling; texts that differ only in case are one group.
    /// </summary>
    public async Task<IReadOnlyList<RedundancyGroupUse>> GroupsAsync(Guid ciId) =>
        [.. (await db.QueryAsync<string>(GroupsSql, new { id = ciId, tenant = Tenants.Default }))
            .GroupBy(g => g, RedundancyGroups.Comparer)
            .Select(g => new RedundancyGroupUse(
                g.GroupBy(s => s, StringComparer.Ordinal).OrderByDescending(s => s.Count()).ThenBy(s => s.Key, StringComparer.Ordinal).First().Key,
                g.Count()))
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.Name, StringComparer.Ordinal)];

    public Task InsertAsync(Guid id, Guid from, Guid to, int typeId, string? group, Guid changedBy) =>
        db.ExecuteAsync(InsertSql, new { id, tenant = Tenants.Default, from, to, typeId, group, changedBy = changedBy.ToString("D") });

    public Task UpdateAsync(Guid id, string? group, Guid changedBy) =>
        db.ExecuteAsync(UpdateSql, new { id, tenant = Tenants.Default, group, changedBy = changedBy.ToString("D") });

    public Task DeleteAsync(Guid id) => db.ExecuteAsync(DeleteSql, Parameters(id));

    private static object Parameters(Guid id) => new { take = 1, id, tenant = Tenants.Default };

    private static RelationshipDetail ToDetail(Row row) =>
        new(row.Id, row.Type, new RelationshipEnd(row.FromId, row.FromName, row.FromType, row.FromState),
            new RelationshipEnd(row.ToId, row.ToName, row.ToType, row.ToState), row.RedundancyGroup, row.CreatedAt, row.ValidFrom,
            Guid.TryParse(row.ChangedBy, out var by) ? new CiChangedBy(by, row.ChangedByName) : null, CiStore.Stamp(row.ValidFrom));

    private sealed record Row(Guid Id, string Type, string? RedundancyGroup, DateTime? CreatedAt, DateTime ValidFrom, string ChangedBy,
        string? ChangedByName, Guid FromId, string FromName, string FromType, string FromState, Guid ToId, string ToName, string ToType,
        string ToState);
}

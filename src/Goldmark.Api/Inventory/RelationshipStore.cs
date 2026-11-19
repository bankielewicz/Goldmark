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
/// relationship's ID, and for a past moment that moment, as an opaque base64url JSON array. A cursor of the other
/// section or of another moment is refused.
/// </summary>
internal sealed record RelationshipCursor(string Direction, string Type, string Name, Guid Id, string? AsOf = null)
{
    public string Encode() =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(AsOf is null
                ? new[] { Direction, Type, Name, Id.ToString("D") }
                : [Direction, Type, Name, Id.ToString("D"), AsOf]))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static RelationshipCursor? Decode(string value, string direction, DateTime? asOf = null)
    {
        try
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            base64 += new string('=', (4 - (base64.Length % 4)) % 4);
            var parts = JsonSerializer.Deserialize<string?[]>(Convert.FromBase64String(base64));
            var moment = Inventory.AsOf.Stamp(asOf);
            return parts is [{ } d, { } type, { } name, { } id, .. var rest] && d == direction && Guid.TryParseExact(id, "D", out var guid)
                && (moment is null ? rest.Length == 0 : rest is [{ } then] && then == moment)
                ? new RelationshipCursor(d, type, name, guid, moment)
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

    // The same tables as they were at @at (US-42, FR-IM-4): the relationships in force then, between the CIs as
    // they were named then, under the type names of then. A relationship ended since is there; one added since is not.
    private const string TablesAsOf =
        "\nFROM cmdb.Relationship" + AsOf.Clause + " AS r"
        + "\nJOIN cmdb.RelationshipType" + AsOf.Clause + " AS rt ON rt.Id = r.RelationshipTypeId"
        + "\nJOIN cmdb.Ci" + AsOf.Clause + " AS f ON f.Id = r.FromCiId"
        + "\nJOIN cmdb.CiType" + AsOf.Clause + " AS ft ON ft.Id = f.CiTypeId"
        + "\nJOIN cmdb.Ci" + AsOf.Clause + " AS t ON t.Id = r.ToCiId"
        + "\nJOIN cmdb.CiType" + AsOf.Clause + " AS tt ON tt.Id = t.CiTypeId"
        + "\nLEFT JOIN cmdb.UserAccount AS u ON u.Id = TRY_CAST(r.ChangedBy AS uniqueidentifier)";

    // A section is sorted by relationship type, then the related CI's name (screen 5's tab; Notes board).
    private const string DependenciesPage = """

        WHERE r.FromCiId = @id AND r.TenantId = @tenant
            AND (@afterId IS NULL OR rt.Name COLLATE Latin1_General_100_CI_AS > @afterType
                OR (rt.Name COLLATE Latin1_General_100_CI_AS = @afterType AND (t.Name COLLATE Latin1_General_100_CI_AS > @afterName
                    OR (t.Name COLLATE Latin1_General_100_CI_AS = @afterName AND r.Id > @afterId))))
        ORDER BY rt.Name COLLATE Latin1_General_100_CI_AS, t.Name COLLATE Latin1_General_100_CI_AS, r.Id;
        """;

    private const string DependentsPage = """

        WHERE r.ToCiId = @id AND r.TenantId = @tenant
            AND (@afterId IS NULL OR rt.Name COLLATE Latin1_General_100_CI_AS > @afterType
                OR (rt.Name COLLATE Latin1_General_100_CI_AS = @afterType AND (f.Name COLLATE Latin1_General_100_CI_AS > @afterName
                    OR (f.Name COLLATE Latin1_General_100_CI_AS = @afterName AND r.Id > @afterId))))
        ORDER BY rt.Name COLLATE Latin1_General_100_CI_AS, f.Name COLLATE Latin1_General_100_CI_AS, r.Id;
        """;

    private const string DependenciesSql = Columns + " FROM cmdb.Relationship AS r" + Joins + DependenciesPage;

    private const string DependentsSql = Columns + " FROM cmdb.Relationship AS r" + Joins + DependentsPage;

    private const string DependenciesAsOfSql = AsOf.Declare + Columns + TablesAsOf + DependenciesPage;

    private const string DependentsAsOfSql = AsOf.Declare + Columns + TablesAsOf + DependentsPage;

    private const string DependenciesCountSql = "SELECT COUNT_BIG(*) FROM cmdb.Relationship WHERE FromCiId = @id AND TenantId = @tenant;";

    private const string DependentsCountSql = "SELECT COUNT_BIG(*) FROM cmdb.Relationship WHERE ToCiId = @id AND TenantId = @tenant;";

    private const string DependenciesCountAsOfSql = AsOf.Declare
        + "SELECT COUNT_BIG(*) FROM cmdb.Relationship" + AsOf.Clause + " WHERE FromCiId = @id AND TenantId = @tenant;";

    private const string DependentsCountAsOfSql = AsOf.Declare
        + "SELECT COUNT_BIG(*) FROM cmdb.Relationship" + AsOf.Clause + " WHERE ToCiId = @id AND TenantId = @tenant;";

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

    // CreatedAt comes from its default (0008), ChangedBy from the connection's acting principal (0009; DbSession).
    private const string InsertSql = """
        INSERT INTO cmdb.Relationship (Id, TenantId, FromCiId, ToCiId, RelationshipTypeId, RedundancyGroup, ChangedBy)
        VALUES (@id, @tenant, @from, @to, @typeId, @group, DEFAULT);
        """;

    private const string UpdateSql = """
        UPDATE cmdb.Relationship SET RedundancyGroup = @group, ChangedBy = DEFAULT
        WHERE Id = @id AND TenantId = @tenant;
        """;

    // DEC-046 item 1: the row goes and its history keeps it as it was. DEC-047 item 9 (owner, 2026-11-19, reversing
    // that item's "never set ChangedBy first"): the delete first stamps the row with the person ending it, in the same
    // transaction, which leaves a last version that lasted no time. The history table keeps it for good, so who ended
    // a relationship outlives the audit log's retention; no FOR SYSTEM_TIME read ever returns it (measured,
    // ChangedByTests), so no "as of" view shows a relationship changed by the person who ended it.
    private const string DeleteSql = """
        UPDATE cmdb.Relationship SET ChangedBy = DEFAULT WHERE Id = @id AND TenantId = @tenant;
        DELETE FROM cmdb.Relationship WHERE Id = @id AND TenantId = @tenant;
        """;

    /// <summary>One page of a section, now or as it was at <paramref name="asOf"/>; the total with the first page.</summary>
    public async Task<RelationshipPage> SectionAsync(Guid ciId, string direction, int take, RelationshipCursor? after, DateTime? asOf = null)
    {
        var dependencies = direction == RelationshipDirections.Dependencies;
        var parameters = new
        {
            asOf = AsOf.Stamp(asOf),
            take = take + 1,
            id = ciId,
            tenant = Tenants.Default,
            afterId = after?.Id,
            afterType = after?.Type,
            afterName = after?.Name,
        };
        var rows = (dependencies, asOf is null) switch
        {
            (true, true) => await db.QueryAsync<Row>(DependenciesSql, parameters),
            (false, true) => await db.QueryAsync<Row>(DependentsSql, parameters),
            (true, false) => await db.QueryAsync<Row>(DependenciesAsOfSql, parameters),
            (false, false) => await db.QueryAsync<Row>(DependentsAsOfSql, parameters),
        };
        var items = rows.Take(take).Select(ToDetail).ToList();
        var next = rows.Count > take && items[^1] is var last
            ? new RelationshipCursor(direction, last.Type, dependencies ? last.To.Name : last.From.Name, last.Id, parameters.asOf).Encode()
            : null;
        long? total = null;
        if (after is null)
        {
            total = (dependencies, asOf is null) switch
            {
                (true, true) => await db.ExecuteScalarAsync<long>(DependenciesCountSql, parameters),
                (false, true) => await db.ExecuteScalarAsync<long>(DependentsCountSql, parameters),
                (true, false) => await db.ExecuteScalarAsync<long>(DependenciesCountAsOfSql, parameters),
                (false, false) => await db.ExecuteScalarAsync<long>(DependentsCountAsOfSql, parameters),
            };
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

    public Task InsertAsync(Guid id, Guid from, Guid to, int typeId, string? group) =>
        db.ExecuteAsync(InsertSql, new { id, tenant = Tenants.Default, from, to, typeId, group });

    public Task UpdateAsync(Guid id, string? group) =>
        db.ExecuteAsync(UpdateSql, new { id, tenant = Tenants.Default, group });

    /// <summary>Ends the relationship, recording who did (see <c>DeleteSql</c>). One transaction, or the stamp would be a version of its own.</summary>
    public Task DeleteAsync(Guid id) => db.InTransactionAsync(() => db.ExecuteAsync(DeleteSql, Parameters(id)));

    private static object Parameters(Guid id) => new { take = 1, id, tenant = Tenants.Default };

    private static RelationshipDetail ToDetail(Row row) =>
        new(row.Id, row.Type, new RelationshipEnd(row.FromId, row.FromName, row.FromType, row.FromState),
            new RelationshipEnd(row.ToId, row.ToName, row.ToType, row.ToState), row.RedundancyGroup, row.CreatedAt, row.ValidFrom,
            Guid.TryParse(row.ChangedBy, out var by) ? new CiChangedBy(by, row.ChangedByName) : null, CiStore.Stamp(row.ValidFrom));

    private sealed record Row(Guid Id, string Type, string? RedundancyGroup, DateTime? CreatedAt, DateTime ValidFrom, string ChangedBy,
        string? ChangedByName, Guid FromId, string FromName, string FromType, string FromState, Guid ToId, string ToName, string ToType,
        string ToState);
}

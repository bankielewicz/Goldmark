using System.Text.Json;
using Goldmark.Api.Data;

namespace Goldmark.Api.Inventory;

/// <summary>The one tenant of the MVP (0001; PRD §9.2).</summary>
internal static class Tenants
{
    public static readonly Guid Default = new("00000000-0000-0000-0000-000000000001");
}

/// <summary>
/// An attribute's kind (US-10): stored as 0006's names, sent and read in the API as <c>string</c>,
/// <c>number</c>, <c>bool</c>, <c>date</c>, <c>enum</c> and <c>ciRef</c>.
/// </summary>
internal static class AttributeDataTypes
{
    public const string String = "String";
    public const string Number = "Number";
    public const string Bool = "Bool";
    public const string Date = "Date";
    public const string Enum = "Enum";
    public const string CiRef = "CiRef";

    public static string? FromApi(string? value) => value switch
    {
        "string" => String,
        "number" => Number,
        "bool" => Bool,
        "date" => Date,
        "enum" => Enum,
        "ciRef" => CiRef,
        _ => null,
    };

    public static string ToApi(string dataType) => dataType switch
    {
        String => "string",
        Number => "number",
        Bool => "bool",
        Date => "date",
        Enum => "enum",
        CiRef => "ciRef",
        _ => dataType,
    };
}

/// <summary>
/// An attribute of a CI type; its value is stored in <c>Ci.Attributes</c> under <see cref="Name"/>. <c>ETag</c>
/// is its version (its <c>ValidFrom</c>, DEC-045 item 8); <c>ChangedBy</c> is set once someone has changed it.
/// </summary>
internal sealed record AttributeDefinition(int Id, string Name, string Label, string DataType, bool Required,
    IReadOnlyList<string>? Choices, string? RefType, bool BuiltIn, string ETag = "", CiChangedBy? ChangedBy = null, DateTime? ChangedAt = null)
{
    /// <summary>The kind as the API names it.</summary>
    public string Kind => AttributeDataTypes.ToApi(DataType);
}

internal sealed record CiTypeInfo(int Id, string Name, string? Category, string? Icon, bool BuiltIn, IReadOnlyList<AttributeDefinition> Attributes,
    bool Hidden = false);

/// <summary>A relationship type (PRD §9.4), dependent → dependency; a null end is any type.</summary>
internal sealed record RelationshipTypeInfo(int Id, string Name, string? From, string? To, string Impact, bool BuiltIn, string? Description,
    string ETag, CiChangedBy? ChangedBy, DateTime? ChangedAt);

/// <summary>How many CIs a type has (retired ones included), and how many have a value for each attribute and each choice.</summary>
internal sealed record TypeUsage(string Name, long Cis, IReadOnlyList<AttributeUsage> Attributes);

internal sealed record AttributeUsage(string Name, long Values, IReadOnlyDictionary<string, long>? Choices);

internal sealed record RelationshipTypeUsage(string Name, long Relationships);

/// <summary>
/// The CI types, their attributes and the relationship types (US-10; DEC-044 items 3, 4 and 8; DEC-045): read on
/// each request, since an Admin may change them at any time, and small enough to read whole. All three tables are
/// system-versioned (0007), so a definition's earlier versions and deleted definitions stay in their history.
/// <para>
/// A type's row is the lock for its definitions. A CI save takes it shared and reads the definitions inside its
/// transaction (<see cref="LockForSaveAsync"/>); a change to the type or its attributes takes it exclusively first
/// (<see cref="LockForChangeAsync"/>). So a save never stores a value for an attribute deleted meanwhile, a choice
/// removed meanwhile or a type hidden meanwhile, and a change never misses a value a save is about to store.
/// </para>
/// </summary>
internal sealed class CiTypeCatalog(DbSession db)
{
    private const string TypeColumns = "SELECT Id, Name, Category, Icon, IsBuiltIn, IsHidden FROM cmdb.CiType";

    private const string TypesSql = TypeColumns + " ORDER BY Id;";

    // Shared until the transaction ends (a CI save, or a definition that names this type); HOLDLOCK takes it
    // whatever the isolation level.
    private const string TypeForSaveSql = TypeColumns + " WITH (HOLDLOCK) WHERE Id = @typeId;";

    // Exclusive until the transaction ends: waits for the saves in progress and holds back new ones. A SELECT,
    // not a dummy UPDATE, so the type's history gets no version that changes nothing. Reads wait as well: every
    // search, CI page and new CI reads the whole type list (ListAsync scans this table), so every inventory page
    // of every type waits until the change commits. An attribute delete rewrites the type's CIs inside it: on
    // 25,000 synthetic CIs (10,000 VMs) about 0.1 s with 4,000 or 10,000 values, and the rewrite escalated to a
    // table lock on cmdb.Ci both times (PerformanceTests, 2026-10-24). A wait past the 30 s command timeout
    // would answer 500.
    private const string TypeForChangeSql = TypeColumns + " WITH (XLOCK, HOLDLOCK, ROWLOCK) WHERE Id = @typeId;";

    private const string AttributeColumns = """
        SELECT a.Id, a.CiTypeId, a.Name, a.Label, a.DataType, a.IsRequired, CAST(a.EnumValues AS nvarchar(max)) AS EnumValues,
            r.Name AS RefType, a.IsBuiltIn, a.ValidFrom, a.ChangedBy, u.DisplayName AS ChangedByName
        FROM cmdb.AttributeDef AS a
        LEFT JOIN cmdb.CiType AS r ON r.Id = a.RefCiTypeId
        LEFT JOIN cmdb.UserAccount AS u ON u.Id = TRY_CAST(a.ChangedBy AS uniqueidentifier)
        """;

    private const string AttributesSql = AttributeColumns + " ORDER BY a.CiTypeId, a.IsBuiltIn DESC, a.SortOrder, a.Id;";

    private const string AttributesOfTypeSql = AttributeColumns + " WHERE a.CiTypeId = @typeId ORDER BY a.IsBuiltIn DESC, a.SortOrder, a.Id;";

    private const string RelationshipColumns = """
        SELECT r.Id, r.Name, f.Name AS FromType, t.Name AS ToType, r.ImpactPropagation, r.IsBuiltIn, r.Description, r.ValidFrom,
            r.ChangedBy, u.DisplayName AS ChangedByName
        """;

    private const string RelationshipJoins = """

        LEFT JOIN cmdb.CiType AS f ON f.Id = r.FromCiTypeId
        LEFT JOIN cmdb.CiType AS t ON t.Id = r.ToCiTypeId
        LEFT JOIN cmdb.UserAccount AS u ON u.Id = TRY_CAST(r.ChangedBy AS uniqueidentifier)
        """;

    private const string RelationshipTypesSql = RelationshipColumns + " FROM cmdb.RelationshipType AS r" + RelationshipJoins + " ORDER BY r.Id;";

    private const string RelationshipTypeForChangeSql = RelationshipColumns + " FROM cmdb.RelationshipType AS r WITH (UPDLOCK, HOLDLOCK)"
        + RelationshipJoins + " WHERE r.Name = @name;";

    // A name in any case (the API finds types in any case), checked under a range lock so two Admins can't both add it.
    private const string AddTypeSql = """
        INSERT INTO cmdb.CiType (Name, IsBuiltIn, Icon, Category)
        OUTPUT INSERTED.Id
        SELECT @name, 0, @icon, @category
        WHERE NOT EXISTS (SELECT 1 FROM cmdb.CiType WITH (UPDLOCK, HOLDLOCK) WHERE Name COLLATE Latin1_General_100_CI_AS = @name);
        """;

    private const string SetHiddenSql = "UPDATE cmdb.CiType SET IsHidden = @hidden WHERE Id = @typeId;";

    // CIs of every lifecycle state: retired ones keep their type (DEC-045 item 1).
    private const string TypeCisSql = "SELECT COUNT_BIG(*) FROM cmdb.Ci WHERE CiTypeId = @typeId;";

    // The type's own attributes go with it, so only another type's link counts.
    private const string TypeReferencesSql = """
        SELECT a.Label, t.Name AS Type, NULL AS RelationshipType
        FROM cmdb.AttributeDef AS a JOIN cmdb.CiType AS t ON t.Id = a.CiTypeId
        WHERE a.RefCiTypeId = @typeId AND a.CiTypeId <> @typeId
        UNION ALL
        SELECT NULL, NULL, r.Name FROM cmdb.RelationshipType AS r WHERE r.FromCiTypeId = @typeId OR r.ToCiTypeId = @typeId;
        """;

    private const string DeleteTypeSql = """
        DELETE FROM cmdb.AttributeDef WHERE CiTypeId = @typeId;
        DELETE FROM cmdb.CiType WHERE Id = @typeId;
        """;

    // The next position after the type's last attribute; the type's lock keeps two additions apart.
    private const string AddAttributeSql = """
        INSERT INTO cmdb.AttributeDef (CiTypeId, Name, Label, DataType, IsRequired, EnumValues, RefCiTypeId, SortOrder, CreatedBy)
        OUTPUT INSERTED.Id
        SELECT @typeId, @name, @label, @dataType, @required, @enumValues, @refTypeId,
            ISNULL((SELECT MAX(SortOrder) FROM cmdb.AttributeDef WITH (UPDLOCK, HOLDLOCK) WHERE CiTypeId = @typeId), 0) + 1,
            @createdBy;
        """;

    private const string ChangeAttributeSql = """
        UPDATE cmdb.AttributeDef SET Label = @label, IsRequired = @required, EnumValues = @enumValues, ChangedBy = @changedBy
        WHERE Id = @id;
        """;

    // The value of one attribute of a type's CIs, as screen 14's counts and the choice check need it. JSON keys are
    // compared as written (OPENJSON's keys are binary).
    private const string ChoiceCountsSql = """
        SELECT CAST(k.[value] AS nvarchar(100)) AS Choice, COUNT_BIG(*) AS Count
        FROM cmdb.Ci AS c CROSS APPLY OPENJSON(c.Attributes) AS k
        WHERE c.CiTypeId = @typeId AND k.[key] = @name COLLATE Latin1_General_BIN2 AND k.[type] = 1
        GROUP BY CAST(k.[value] AS nvarchar(100));
        """;

    // Deleting an attribute takes its value out of every CI of the type in one statement (DEC-045 items 2 and 6): each
    // CI gets a new version by the Admin, and its history keeps the value. JSON_MODIFY leaves the other values as
    // written, numbers digit for digit.
    private const string RemoveValuesSql = """
        UPDATE cmdb.Ci SET Attributes = JSON_MODIFY(Attributes, CONCAT('$."', @name, '"'), NULL), ChangedBy = @changedBy
        WHERE CiTypeId = @typeId AND JSON_PATH_EXISTS(Attributes, CONCAT('$."', @name, '"')) = 1;
        """;

    private const string DeleteAttributeSql = "DELETE FROM cmdb.AttributeDef WHERE Id = @id;";

    private const string TypeCountsSql = "SELECT CiTypeId, COUNT_BIG(*) AS Count FROM cmdb.Ci GROUP BY CiTypeId;";

    private const string AttributeCountsSql = """
        SELECT c.CiTypeId, k.[key] AS Name, COUNT_BIG(*) AS Count
        FROM cmdb.Ci AS c CROSS APPLY OPENJSON(c.Attributes) AS k
        GROUP BY c.CiTypeId, k.[key];
        """;

    private const string AllChoiceCountsSql = """
        SELECT a.CiTypeId, a.Name, CAST(k.[value] AS nvarchar(100)) AS Choice, COUNT_BIG(*) AS Count
        FROM cmdb.AttributeDef AS a
        JOIN cmdb.Ci AS c ON c.CiTypeId = a.CiTypeId
        CROSS APPLY OPENJSON(c.Attributes) AS k
        WHERE a.DataType = 'Enum' AND k.[key] = a.Name COLLATE Latin1_General_BIN2 AND k.[type] = 1
        GROUP BY a.CiTypeId, a.Name, CAST(k.[value] AS nvarchar(100));
        """;

    private const string RelationshipCountsSql = "SELECT RelationshipTypeId, COUNT_BIG(*) AS Count FROM cmdb.Relationship GROUP BY RelationshipTypeId;";

    private const string RelationshipsOfTypeSql = "SELECT COUNT_BIG(*) FROM cmdb.Relationship WHERE RelationshipTypeId = @id;";

    private const string AddRelationshipTypeSql = """
        INSERT INTO cmdb.RelationshipType (Name, FromCiTypeId, ToCiTypeId, ImpactPropagation, Description, ChangedBy)
        OUTPUT INSERTED.Id
        VALUES (@name, @fromTypeId, @toTypeId, @impact, @description, @changedBy);
        """;

    private const string ChangeRelationshipTypeSql = """
        UPDATE cmdb.RelationshipType SET ImpactPropagation = @impact, Description = @description, ChangedBy = @changedBy
        WHERE Id = @id;
        """;

    private const string DeleteRelationshipTypeSql = "DELETE FROM cmdb.RelationshipType WHERE Id = @id;";

    public async Task<IReadOnlyList<CiTypeInfo>> ListAsync()
    {
        var types = await db.QueryAsync<TypeRow>(TypesSql);
        var attributes = (await db.QueryAsync<AttributeRow>(AttributesSql)).ToLookup(a => a.CiTypeId);
        return [.. types.Select(t => Type(t, attributes[t.Id]))];
    }

    /// <summary>A type by name, in any case (the API's <c>type</c> values).</summary>
    public static CiTypeInfo? Find(IReadOnlyList<CiTypeInfo> types, string? name) =>
        name is null ? null : types.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The type as it is now, locked (shared) until the transaction ends; null if it was deleted. Call inside one.</summary>
    public Task<CiTypeInfo?> LockForSaveAsync(int typeId) => LockedAsync(TypeForSaveSql, typeId);

    /// <summary>The type as it is now, locked (exclusive) until the transaction ends; null if it was deleted. Call inside one, first.</summary>
    public Task<CiTypeInfo?> LockForChangeAsync(int typeId) => LockedAsync(TypeForChangeSql, typeId);

    private async Task<CiTypeInfo?> LockedAsync([System.Diagnostics.CodeAnalysis.ConstantExpected] string sql, int typeId)
    {
        if (await db.QuerySingleOrDefaultAsync<TypeRow>(sql, new { typeId }) is not { } row)
        {
            return null;
        }

        return Type(row, await db.QueryAsync<AttributeRow>(AttributesOfTypeSql, new { typeId }));
    }

    public async Task<IReadOnlyList<RelationshipTypeInfo>> RelationshipTypesAsync() =>
        [.. (await db.QueryAsync<RelationshipRow>(RelationshipTypesSql)).Select(RelationshipType)];

    /// <summary>The relationship type, locked until the transaction ends; null if there's none of that name.</summary>
    public async Task<RelationshipTypeInfo?> LockRelationshipTypeAsync(string name) =>
        await db.QuerySingleOrDefaultAsync<RelationshipRow>(RelationshipTypeForChangeSql, new { name }) is { } row ? RelationshipType(row) : null;

    /// <summary>The new type's ID, or null when a type of that name (in any case) exists.</summary>
    public Task<int?> AddTypeAsync(string name, string category, string icon) =>
        db.ExecuteScalarAsync<int?>(AddTypeSql, new { name, category, icon });

    public Task SetHiddenAsync(int typeId, bool hidden) => db.ExecuteAsync(SetHiddenSql, new { typeId, hidden });

    public Task<long> CisOfTypeAsync(int typeId) => db.ExecuteScalarAsync<long>(TypeCisSql, new { typeId });

    /// <summary>Another type's attributes that link to this type, and the relationship types that name it.</summary>
    public Task<IReadOnlyList<TypeReference>> ReferencesAsync(int typeId) => db.QueryAsync<TypeReference>(TypeReferencesSql, new { typeId });

    public Task DeleteTypeAsync(int typeId) => db.ExecuteAsync(DeleteTypeSql, new { typeId });

    public async Task<int> AddAttributeAsync(int typeId, string name, string label, string dataType, bool required,
        IReadOnlyList<string>? choices, int? refTypeId, Guid createdBy) =>
        await db.ExecuteScalarAsync<int>(AddAttributeSql, new
        {
            typeId,
            name,
            label,
            dataType,
            required,
            enumValues = choices is null ? null : JsonSerializer.Serialize(choices),
            refTypeId,
            createdBy = createdBy.ToString("D"),
        });

    public Task ChangeAttributeAsync(int id, string label, bool required, IReadOnlyList<string>? choices, Guid changedBy) =>
        db.ExecuteAsync(ChangeAttributeSql, new
        {
            id,
            label,
            required,
            enumValues = choices is null ? null : JsonSerializer.Serialize(choices),
            changedBy = changedBy.ToString("D"),
        });

    /// <summary>How many of the type's CIs (retired ones included) hold each choice of the attribute.</summary>
    public async Task<IReadOnlyDictionary<string, long>> ChoiceCountsAsync(int typeId, string name) =>
        (await db.QueryAsync<ChoiceCount>(ChoiceCountsSql, new { typeId, name })).ToDictionary(c => c.Choice, c => c.Count, StringComparer.Ordinal);

    /// <summary>Takes the attribute's values out of the type's CIs and deletes it; returns how many CIs had a value.</summary>
    public async Task<int> DeleteAttributeAsync(int typeId, AttributeDefinition attribute, Guid changedBy)
    {
        var removed = await db.ExecuteAsync(RemoveValuesSql, new { typeId, name = attribute.Name, changedBy = changedBy.ToString("D") });
        await db.ExecuteAsync(DeleteAttributeSql, new { id = attribute.Id });
        return removed;
    }

    /// <summary>Screen 14's counts: CIs per type (retired ones included), values per attribute and choice, relationships per type.</summary>
    public async Task<(IReadOnlyList<TypeUsage> Types, IReadOnlyList<RelationshipTypeUsage> RelationshipTypes)> UsageAsync(IReadOnlyList<CiTypeInfo> types,
        IReadOnlyList<RelationshipTypeInfo> relationshipTypes)
    {
        var cis = (await db.QueryAsync<TypeCount>(TypeCountsSql)).ToDictionary(c => c.CiTypeId, c => c.Count);
        var values = (await db.QueryAsync<AttributeCount>(AttributeCountsSql)).ToDictionary(c => (c.CiTypeId, c.Name), c => c.Count);
        var choices = (await db.QueryAsync<AttributeChoiceCount>(AllChoiceCountsSql)).ToLookup(c => (c.CiTypeId, c.Name));
        var relationships = (await db.QueryAsync<RelationshipCount>(RelationshipCountsSql)).ToDictionary(c => c.RelationshipTypeId, c => c.Count);
        return (
            [.. types.Select(t => new TypeUsage(t.Name, cis.GetValueOrDefault(t.Id), [.. t.Attributes.Select(a => new AttributeUsage(a.Name,
                values.GetValueOrDefault((t.Id, a.Name)),
                a.DataType == AttributeDataTypes.Enum
                    ? (a.Choices ?? []).ToDictionary(c => c, c => choices[(t.Id, a.Name)].FirstOrDefault(x => x.Choice == c)?.Count ?? 0, StringComparer.Ordinal)
                    : null))]))],
            [.. relationshipTypes.Select(r => new RelationshipTypeUsage(r.Name, relationships.GetValueOrDefault(r.Id)))]);
    }

    public Task<int> AddRelationshipTypeAsync(string name, int? fromTypeId, int? toTypeId, string impact, string? description, Guid changedBy) =>
        db.ExecuteScalarAsync<int>(AddRelationshipTypeSql, new { name, fromTypeId, toTypeId, impact, description, changedBy = changedBy.ToString("D") });

    public Task ChangeRelationshipTypeAsync(int id, string impact, string? description, Guid changedBy) =>
        db.ExecuteAsync(ChangeRelationshipTypeSql, new { id, impact, description, changedBy = changedBy.ToString("D") });

    public Task<long> RelationshipsOfTypeAsync(int id) => db.ExecuteScalarAsync<long>(RelationshipsOfTypeSql, new { id });

    public Task DeleteRelationshipTypeAsync(int id) => db.ExecuteAsync(DeleteRelationshipTypeSql, new { id });

    private static CiTypeInfo Type(TypeRow t, IEnumerable<AttributeRow> attributes) =>
        new(t.Id, t.Name, t.Category, t.Icon, t.IsBuiltIn, [.. attributes.Select(a => new AttributeDefinition(a.Id, a.Name, a.Label, a.DataType, a.IsRequired,
            a.EnumValues is { } json ? JsonSerializer.Deserialize<string[]>(json) : null, a.RefType, a.IsBuiltIn, CiStore.Stamp(a.ValidFrom),
            Changer(a.ChangedBy, a.ChangedByName), a.ChangedBy is null ? null : a.ValidFrom))], t.IsHidden);

    private static RelationshipTypeInfo RelationshipType(RelationshipRow r) =>
        new(r.Id, r.Name, r.FromType, r.ToType, r.ImpactPropagation, r.IsBuiltIn, r.Description, CiStore.Stamp(r.ValidFrom),
            Changer(r.ChangedBy, r.ChangedByName), r.ChangedBy is null ? null : r.ValidFrom);

    private static CiChangedBy? Changer(string? id, string? displayName) =>
        id is null ? null : new CiChangedBy(Guid.TryParse(id, out var guid) ? guid : null, displayName);

    internal sealed record TypeReference(string? Label, string? Type, string? RelationshipType);

    private sealed record TypeRow(int Id, string Name, string? Category, string? Icon, bool IsBuiltIn, bool IsHidden);

    private sealed record AttributeRow(int Id, int CiTypeId, string Name, string Label, string DataType, bool IsRequired,
        string? EnumValues, string? RefType, bool IsBuiltIn, DateTime ValidFrom, string? ChangedBy, string? ChangedByName);

    private sealed record RelationshipRow(int Id, string Name, string? FromType, string? ToType, string ImpactPropagation, bool IsBuiltIn,
        string? Description, DateTime ValidFrom, string? ChangedBy, string? ChangedByName);

    private sealed record ChoiceCount(string Choice, long Count);

    private sealed record TypeCount(int CiTypeId, long Count);

    private sealed record AttributeCount(int CiTypeId, string Name, long Count);

    private sealed record AttributeChoiceCount(int CiTypeId, string Name, string Choice, long Count);

    private sealed record RelationshipCount(int RelationshipTypeId, long Count);
}

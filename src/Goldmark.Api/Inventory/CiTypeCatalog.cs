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

/// <summary>An attribute of a CI type; its value is stored in <c>Ci.Attributes</c> under <see cref="Name"/>.</summary>
internal sealed record AttributeDefinition(int Id, string Name, string Label, string DataType, bool Required,
    IReadOnlyList<string>? Choices, string? RefType, bool BuiltIn)
{
    /// <summary>The kind as the API names it.</summary>
    public string Kind => AttributeDataTypes.ToApi(DataType);
}

internal sealed record CiTypeInfo(int Id, string Name, string? Category, string? Icon, bool BuiltIn, IReadOnlyList<AttributeDefinition> Attributes);

/// <summary>A relationship type (PRD §9.4), dependent → dependency; a null end is any type.</summary>
internal sealed record RelationshipTypeInfo(int Id, string Name, string? From, string? To, string Impact, bool BuiltIn);

/// <summary>
/// The CI types, their attributes and the relationship types (US-10; DEC-044 items 3, 4 and 8): read on
/// each request, since an Admin may add an attribute at any time, and small enough to read whole.
/// </summary>
internal sealed class CiTypeCatalog(DbSession db)
{
    private const string TypesSql = "SELECT Id, Name, Category, Icon, IsBuiltIn FROM cmdb.CiType ORDER BY Id;";

    private const string AttributesSql = """
        SELECT a.Id, a.CiTypeId, a.Name, a.Label, a.DataType, a.IsRequired, CAST(a.EnumValues AS nvarchar(max)) AS EnumValues,
            r.Name AS RefType, a.IsBuiltIn
        FROM cmdb.AttributeDef AS a
        LEFT JOIN cmdb.CiType AS r ON r.Id = a.RefCiTypeId
        ORDER BY a.CiTypeId, a.IsBuiltIn DESC, a.SortOrder, a.Id;
        """;

    private const string RelationshipTypesSql = """
        SELECT r.Id, r.Name, f.Name AS FromType, t.Name AS ToType, r.ImpactPropagation, r.IsBuiltIn
        FROM cmdb.RelationshipType AS r
        LEFT JOIN cmdb.CiType AS f ON f.Id = r.FromCiTypeId
        LEFT JOIN cmdb.CiType AS t ON t.Id = r.ToCiTypeId
        ORDER BY r.Id;
        """;

    // The next position after the type's last attribute, under a lock so two additions can't share it.
    private const string AddAttributeSql = """
        INSERT INTO cmdb.AttributeDef (CiTypeId, Name, Label, DataType, IsRequired, EnumValues, RefCiTypeId, SortOrder, CreatedBy)
        OUTPUT INSERTED.Id
        SELECT @typeId, @name, @label, @dataType, @required, @enumValues, @refTypeId,
            ISNULL((SELECT MAX(SortOrder) FROM cmdb.AttributeDef WITH (UPDLOCK, HOLDLOCK) WHERE CiTypeId = @typeId), 0) + 1,
            @createdBy;
        """;

    public async Task<IReadOnlyList<CiTypeInfo>> ListAsync()
    {
        var types = await db.QueryAsync<TypeRow>(TypesSql);
        var attributes = (await db.QueryAsync<AttributeRow>(AttributesSql)).ToLookup(a => a.CiTypeId);
        return [.. types.Select(t => new CiTypeInfo(t.Id, t.Name, t.Category, t.Icon, t.IsBuiltIn,
            [.. attributes[t.Id].Select(a => new AttributeDefinition(a.Id, a.Name, a.Label, a.DataType, a.IsRequired,
                a.EnumValues is { } json ? JsonSerializer.Deserialize<string[]>(json) : null, a.RefType, a.IsBuiltIn))]))];
    }

    /// <summary>A type by name, in any case (the API's <c>type</c> values).</summary>
    public static CiTypeInfo? Find(IReadOnlyList<CiTypeInfo> types, string? name) =>
        name is null ? null : types.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    public async Task<IReadOnlyList<RelationshipTypeInfo>> RelationshipTypesAsync() =>
        [.. (await db.QueryAsync<RelationshipRow>(RelationshipTypesSql)).Select(r =>
            new RelationshipTypeInfo(r.Id, r.Name, r.FromType, r.ToType, r.ImpactPropagation, r.IsBuiltIn))];

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

    private sealed record TypeRow(int Id, string Name, string? Category, string? Icon, bool IsBuiltIn);

    private sealed record AttributeRow(int Id, int CiTypeId, string Name, string Label, string DataType, bool IsRequired,
        string? EnumValues, string? RefType, bool IsBuiltIn);

    private sealed record RelationshipRow(int Id, string Name, string? FromType, string? ToType, string ImpactPropagation, bool IsBuiltIn);
}

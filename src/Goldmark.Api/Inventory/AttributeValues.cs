using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Goldmark.Api.Inventory;

/// <summary>
/// The outcome of checking a CI's name or attribute values: the values to store, in the type's order
/// and normalized (text trimmed, dates as <c>yyyy-MM-dd</c>, links as IDs), the CIs they link to, and
/// field errors keyed as the API reports them (<c>name</c>, <c>attributes.costCentre</c>).
/// </summary>
internal sealed record AttributeCheck(JsonObject Values, IReadOnlyDictionary<string, Guid> Links, IReadOnlyDictionary<string, string[]> Errors);

/// <summary>
/// Checks values against a CI type's attribute definitions (US-10, US-11). Only defined attributes are
/// stored; a null, an empty text or a missing value leaves one unset, which a required attribute
/// refuses. A required attribute added to a type that already has CIs is enforced the next time each of
/// them is saved (DEC-044 build notes). Whether a linked CI exists, isn't retired and has the right
/// type needs the database: <see cref="CiStore"/> checks <see cref="AttributeCheck.Links"/>.
/// </summary>
internal static class AttributeValues
{
    public const int MaxNameLength = 400;
    public const int MaxTextLength = 4000;

    /// <summary>The name's error code, or null when it's fine (trimmed, 1 to 400 characters, no control characters).</summary>
    public static string? NameError(string? name) => name?.Trim() switch
    {
        null or "" => "Required",
        { Length: > MaxNameLength } => "TooLong",
        var n when n.Any(char.IsControl) => "InvalidName",
        _ => null,
    };

    public static AttributeCheck Check(CiTypeInfo type, JsonElement? input)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var given = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (input is { ValueKind: JsonValueKind.Object } values)
        {
            foreach (var property in values.EnumerateObject())
            {
                given[property.Name] = property.Value;
            }
        }
        else if (input is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) })
        {
            errors["attributes"] = ["InvalidAttributes"];
        }

        foreach (var name in given.Keys.Where(k => !type.Attributes.Any(a => a.Name == k)))
        {
            errors[Field(name)] = ["UnknownAttribute"];
        }

        var stored = new JsonObject();
        var links = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var attribute in type.Attributes)
        {
            if (!given.TryGetValue(attribute.Name, out var value) || value.ValueKind == JsonValueKind.Null
                || (value.ValueKind == JsonValueKind.String && value.GetString()!.Trim().Length == 0))
            {
                if (attribute.Required)
                {
                    errors[Field(attribute.Name)] = ["Required"];
                }

                continue;
            }

            var (node, error) = Normalize(attribute, value);
            if (error is not null)
            {
                errors[Field(attribute.Name)] = [error];
                continue;
            }

            stored[attribute.Name] = node;
            if (attribute.DataType == AttributeDataTypes.CiRef)
            {
                links[attribute.Name] = Guid.Parse(node!.GetValue<string>());
            }
        }

        return new AttributeCheck(stored, links, errors);
    }

    /// <summary>The labels of the attributes whose stored value differs, in the type's order.</summary>
    public static IEnumerable<string> Changed(CiTypeInfo type, JsonObject before, JsonObject after) =>
        type.Attributes.Where(a => !JsonNode.DeepEquals(before[a.Name], after[a.Name])).Select(a => a.Label);

    public static JsonObject Parse(string json) => JsonNode.Parse(json) as JsonObject ?? [];

    public static string Field(string attribute) => "attributes." + attribute;

    private static (JsonNode? Node, string? Error) Normalize(AttributeDefinition attribute, JsonElement value)
    {
        switch (attribute.DataType)
        {
            case AttributeDataTypes.String when value.ValueKind == JsonValueKind.String:
                var text = value.GetString()!.Trim();
                return text.Length > MaxTextLength ? (null, "TooLong")
                    : text.Any(char.IsControl) ? (null, "InvalidText")
                    : (JsonValue.Create(text), null);
            case AttributeDataTypes.String:
                return (null, "InvalidText");
            case AttributeDataTypes.Number:
                return value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
                    ? (JsonValue.Create(number), null)
                    : (null, "InvalidNumber");
            case AttributeDataTypes.Bool:
                return value.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? (JsonValue.Create(value.GetBoolean()), null)
                    : (null, "InvalidBool");
            case AttributeDataTypes.Date:
                return value.ValueKind == JsonValueKind.String
                    && DateOnly.TryParseExact(value.GetString()!.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? (JsonValue.Create(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), null)
                    : (null, "InvalidDate");
            case AttributeDataTypes.Enum:
                return value.ValueKind == JsonValueKind.String && (attribute.Choices ?? []).Contains(value.GetString()!.Trim(), StringComparer.Ordinal)
                    ? (JsonValue.Create(value.GetString()!.Trim()), null)
                    : (null, "InvalidChoice");
            case AttributeDataTypes.CiRef:
                return value.ValueKind == JsonValueKind.String && Guid.TryParseExact(value.GetString()!.Trim(), "D", out var id)
                    ? (JsonValue.Create(id.ToString("D")), null)
                    : (null, "InvalidLink");
            default:
                return (null, "InvalidValue");
        }
    }
}

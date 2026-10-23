using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

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
internal static partial class AttributeValues
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

    /// <summary>
    /// The stored values as the API sends them: a number as a decimal string with every digit as stored
    /// ("9007199254740993", "1.50"), because a JSON number beyond a double's precision is rounded by
    /// JavaScript and most JSON readers (DEC-044 item 12). Stored, it stays a JSON number.
    /// </summary>
    public static JsonObject ForResponse(CiTypeInfo type, JsonObject stored)
    {
        var response = new JsonObject();
        foreach (var (name, node) in stored)
        {
            var number = type.Attributes.Any(a => a.Name == name && a.DataType == AttributeDataTypes.Number)
                && node is JsonValue { } value && value.GetValueKind() == JsonValueKind.Number;
            response[name] = number ? JsonValue.Create(node!.ToJsonString()) : node?.DeepClone();
        }

        return response;
    }

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
                return value.ValueKind switch
                {
                    JsonValueKind.Number => ExactDecimal(value.GetRawText()),
                    JsonValueKind.String => ExactDecimal(value.GetString()!.Trim()),
                    _ => (null, "InvalidNumber"),
                };
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

    /// <summary>
    /// A number written as a JSON number or a decimal string, kept exactly as a .NET decimal (DEC-044 item 12): one
    /// beyond its range (79,228,162,514,264,337,593,543,950,335 either way) is <c>NumberOutOfRange</c>, one it
    /// would round (more than its 28 to 29 significant digits) is <c>TooPrecise</c>; nothing is rounded silently.
    /// </summary>
    private static (JsonNode? Node, string? Error) ExactDecimal(string text)
    {
        if (!NumberText().IsMatch(text))
        {
            return (null, "InvalidNumber");
        }

        if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return (null, "NumberOutOfRange");
        }

        return Canonical(text) is { } written && written == Canonical(number.ToString(CultureInfo.InvariantCulture))
            ? (JsonValue.Create(number), null)
            : (null, "TooPrecise");
    }

    /// <summary>A number's sign, digits (no leading or trailing zeros) and power of ten, so equal values compare equal; null if the exponent overflows.</summary>
    private static (bool Negative, string Digits, long Exponent)? Canonical(string text)
    {
        var negative = text.StartsWith('-');
        var unsigned = text.TrimStart('+', '-');
        var e = unsigned.IndexOfAny(['e', 'E']);
        long exponent = 0;
        if (e >= 0 && !long.TryParse(unsigned[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent))
        {
            return null;
        }

        var mantissa = e >= 0 ? unsigned[..e] : unsigned;
        var point = mantissa.IndexOf('.', StringComparison.Ordinal);
        var digits = mantissa.Replace(".", "", StringComparison.Ordinal).TrimStart('0');
        exponent -= point >= 0 ? mantissa.Length - point - 1 : 0;
        var trimmed = digits.TrimEnd('0');
        exponent += digits.Length - trimmed.Length;
        return trimmed.Length == 0 ? (false, "", 0) : (negative, trimmed, exponent);
    }

    [GeneratedRegex(@"^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex NumberText();
}

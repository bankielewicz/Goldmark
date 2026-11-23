using System.Text.Json;
using System.Text.Json.Nodes;

namespace Goldmark.Api.Inventory;

/// <summary>
/// A JSON Merge Patch of a CI (RFC 7396; <c>PATCH /api/v1/cis/{id}</c>, #48, DEC-044 item 11, DEC-048): a member left out
/// keeps its value. <c>name</c>: a text replaces the name, which can't be cleared (<c>null</c> is <c>Required</c>, as an
/// empty name is). <c>attributes</c>: an object merges by attribute, a value replacing the attribute's, <c>null</c>
/// clearing it, and an attribute not named keeping its value; <c>null</c> clears them all. Any other member is refused
/// (<c>UnknownField</c>), since a misspelt one would otherwise change nothing without a word. The merged values are then
/// checked as PUT's are (<see cref="AttributeValues.Check"/>), an attribute the type doesn't have included. The two
/// members' names match in any case, as the API's JSON binding matches PUT's; attribute names match exactly.
/// </summary>
internal static class CiMergePatch
{
    /// <summary>The one media type a CI's PATCH takes.</summary>
    public const string MediaType = "application/merge-patch+json";

    /// <summary>
    /// The name and values <paramref name="patch"/>, a JSON object, asks for, from the CI's <paramref name="name"/> and
    /// <paramref name="stored"/> values (which it leaves as they are), with the patch's own field errors.
    /// </summary>
    public static (string? Name, JsonElement Attributes, Dictionary<string, string[]> Errors) Apply(JsonElement patch, string name, JsonObject stored)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        string? merged = name;
        var values = (JsonObject)stored.DeepClone();
        foreach (var member in patch.EnumerateObject())
        {
            if (member.Name.Equals("name", StringComparison.OrdinalIgnoreCase))
            {
                switch (member.Value.ValueKind)
                {
                    case JsonValueKind.String:
                        merged = member.Value.GetString();
                        break;
                    case JsonValueKind.Null:
                        merged = null;
                        break;
                    default:
                        errors["name"] = ["InvalidName"];
                        break;
                }
            }
            else if (member.Name.Equals("attributes", StringComparison.OrdinalIgnoreCase))
            {
                switch (member.Value.ValueKind)
                {
                    case JsonValueKind.Object:
                        foreach (var attribute in member.Value.EnumerateObject())
                        {
                            if (attribute.Value.ValueKind == JsonValueKind.Null)
                            {
                                values.Remove(attribute.Name);
                            }
                            else
                            {
                                values[attribute.Name] = JsonNode.Parse(attribute.Value.GetRawText());
                            }
                        }

                        break;
                    case JsonValueKind.Null:
                        values.Clear();
                        break;
                    default:
                        errors["attributes"] = ["InvalidAttributes"];
                        break;
                }
            }
            else
            {
                errors[member.Name] = ["UnknownField"];
            }
        }

        return (merged, JsonSerializer.SerializeToElement(values), errors);
    }
}

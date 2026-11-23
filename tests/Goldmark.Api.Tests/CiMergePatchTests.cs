using System.Text.Json;
using System.Text.Json.Nodes;
using Goldmark.Api.Inventory;

namespace Goldmark.Api.Tests;

/// <summary>A CI's partial save as a JSON Merge Patch (RFC 7396; #48, DEC-044 item 11, DEC-048), before PUT's checks.</summary>
public sealed class CiMergePatchTests
{
    private const string Stored = """{"costCentre":"CC-4410","vcpusReserved":8,"monitored":true,"backupTier":"Gold"}""";

    [Fact]
    public void A_member_left_out_keeps_its_value()
    {
        var (name, attributes, errors) = Apply("{}");

        Assert.Equal("app-erp-db01", name);
        Assert.Equal(Stored, attributes.GetRawText());
        Assert.Empty(errors);
    }

    [Fact]
    public void A_name_replaces_the_name_and_null_asks_for_none_which_the_check_refuses_as_Required()
    {
        Assert.Equal("app-erp-db02", Apply("""{"name":"app-erp-db02"}""").Name);
        Assert.Null(Apply("""{"name":null}""").Name);
        Assert.Equal("Required", AttributeValues.NameError(Apply("""{"name":null}""").Name));
    }

    [Theory]
    [InlineData("""{"name":5}""")]
    [InlineData("""{"name":{"text":"x"}}""")]
    [InlineData("""{"name":["x"]}""")]
    [InlineData("""{"name":true}""")]
    public void A_name_that_is_no_text_is_InvalidName_and_the_name_stays(string patch)
    {
        var (name, _, errors) = Apply(patch);

        Assert.Equal("app-erp-db01", name);
        Assert.Equal(["InvalidName"], errors["name"]);
    }

    [Fact]
    public void Attributes_merge_one_by_one_a_value_replacing_null_clearing_and_one_not_named_staying()
    {
        var (_, attributes, errors) = Apply("""{"attributes":{"backupTier":"Silver","monitored":null,"patchWindowStarts":"2026-11-07"}}""");

        Assert.Equal("""{"costCentre":"CC-4410","vcpusReserved":8,"backupTier":"Silver","patchWindowStarts":"2026-11-07"}""", attributes.GetRawText());
        Assert.Empty(errors);
    }

    [Fact]
    public void Attributes_null_clears_every_value()
    {
        var (_, attributes, errors) = Apply("""{"attributes":null}""");

        Assert.Equal("{}", attributes.GetRawText());
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("""{"attributes":"costCentre"}""")]
    [InlineData("""{"attributes":[]}""")]
    [InlineData("""{"attributes":5}""")]
    [InlineData("""{"attributes":false}""")]
    public void Attributes_that_are_no_object_are_InvalidAttributes_and_the_values_stay(string patch)
    {
        var (_, attributes, errors) = Apply(patch);

        Assert.Equal(Stored, attributes.GetRawText());
        Assert.Equal(["InvalidAttributes"], errors["attributes"]);
    }

    [Fact]
    public void Any_other_member_is_UnknownField()
    {
        var (name, attributes, errors) = Apply("""{"nmae":"app-erp-db02","type":"Server","eTag":"x","attributes":{"backupTier":"Bronze"}}""");

        Assert.Equal(["eTag", "nmae", "type"], errors.Keys.Order(StringComparer.Ordinal));
        Assert.All(errors.Values, codes => Assert.Equal(["UnknownField"], codes));
        Assert.Equal("app-erp-db01", name);
        Assert.Contains("\"backupTier\":\"Bronze\"", attributes.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_two_members_match_in_any_case_and_attribute_names_exactly()
    {
        var (name, attributes, errors) = Apply("""{"Name":"app-erp-db02","ATTRIBUTES":{"BackupTier":"Bronze"}}""");

        Assert.Equal("app-erp-db02", name);
        Assert.Empty(errors);
        // Merged as written: the check that follows refuses "BackupTier" as UnknownAttribute, as PUT's does.
        Assert.Equal("""{"costCentre":"CC-4410","vcpusReserved":8,"monitored":true,"backupTier":"Gold","BackupTier":"Bronze"}""", attributes.GetRawText());
    }

    [Fact]
    public void Numbers_keep_every_digit_whether_stored_or_sent()
    {
        var stored = JsonNode.Parse("""{"capacityBytes":9007199254740993,"ratio":1.50}""")!.AsObject();
        using var patch = JsonDocument.Parse("""{"attributes":{"ratio":0.1234567890123456789,"limit":"79228162514264337593543950335"}}""");

        var (_, attributes, _) = CiMergePatch.Apply(patch.RootElement, "array-01", stored);

        Assert.Equal("""{"capacityBytes":9007199254740993,"ratio":0.1234567890123456789,"limit":"79228162514264337593543950335"}""", attributes.GetRawText());
    }

    [Fact]
    public void The_stored_values_are_left_as_they_are_for_the_comparison_that_follows()
    {
        var stored = JsonNode.Parse(Stored)!.AsObject();
        using var patch = JsonDocument.Parse("""{"attributes":{"backupTier":"Silver","monitored":null}}""");

        CiMergePatch.Apply(patch.RootElement, "app-erp-db01", stored);

        Assert.Equal(Stored, stored.ToJsonString());
    }

    private static (string? Name, JsonElement Attributes, Dictionary<string, string[]> Errors) Apply(string patch)
    {
        using var document = JsonDocument.Parse(patch);
        var (name, attributes, errors) = CiMergePatch.Apply(document.RootElement, "app-erp-db01", JsonNode.Parse(Stored)!.AsObject());
        return (name, attributes, errors);
    }
}

using Goldmark.Api.Audit;
using Goldmark.Api.Inventory;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Goldmark.Api.Tests;

/// <summary>
/// Slice 6's rules that need no database (DEC-046): the redundancy group text (item 2), the tab's cursor, the graph
/// read's parameters (items 4, 5 and 10), the export's file and rows (item 7) and the audit filter on a relationship.
/// </summary>
public sealed class RelationshipRulesTests
{
    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", null, null)]
    [InlineData("   ", null, null)]
    [InlineData("  AG-ERP ", "AG-ERP", null)]
    [InlineData("vSwitch0\tuplinks", null, "InvalidText")]
    [InlineData("line\nbreak", null, "InvalidText")]
    public void A_redundancy_group_is_trimmed_an_empty_one_is_none_and_control_characters_are_refused(string? value, string? group, string? error) =>
        Assert.Equal((group, error), RedundancyGroups.Parse(value));

    [Fact]
    public void A_redundancy_group_has_at_most_200_characters_and_texts_differing_only_in_case_are_one_group()
    {
        Assert.Equal((new string('g', 200), (string?)null), RedundancyGroups.Parse(new string('g', 200)));
        Assert.Equal(((string?)null, "TooLong"), RedundancyGroups.Parse(new string('g', 201)));

        Assert.True(RedundancyGroups.Comparer.Equals("AG-ERP", "ag-erp"));
        Assert.False(RedundancyGroups.Comparer.Equals("AG-ERP", "AG ERP"));
        Assert.False(RedundancyGroups.Comparer.Equals("vSwitch0", "vSwitch0 ")); // never otherwise normalised
        Assert.False(RedundancyGroups.Comparer.Equals("Zürich", "Zurich"));
        Assert.False(RedundancyGroups.Comparer.Equals("ＡＧ", "AG")); // full-width letters, which a SQL collation would fold
    }

    [Fact]
    public void A_cursor_belongs_to_its_section()
    {
        var cursor = new RelationshipCursor(RelationshipDirections.Dependents, "runs_on", "vm-050", Guid.CreateVersion7());

        Assert.Equal(cursor, RelationshipCursor.Decode(cursor.Encode(), RelationshipDirections.Dependents));
        Assert.Null(RelationshipCursor.Decode(cursor.Encode(), RelationshipDirections.Dependencies));
        Assert.Null(RelationshipCursor.Decode("not-a-cursor", RelationshipDirections.Dependents));
        Assert.Null(RelationshipCursor.Decode("WyJkZXBlbmRlbnRzIl0", RelationshipDirections.Dependents)); // ["dependents"]
    }

    [Fact]
    public void The_graph_read_defaults_to_three_hops_both_ways_retired_left_out_and_grouping_on()
    {
        var ci = Guid.CreateVersion7();

        var (query, errors) = GraphQuery.Parse(Query(("ci", ci.ToString("D"))), Types, RelationshipTypes);

        Assert.Empty(errors);
        Assert.Equal(new GraphQuery(ci, 3, "both", [], [], Retired: false, Group: true) with { Types = query!.Types, RelationshipTypes = query.RelationshipTypes }, query);
        Assert.Empty(query.Types);
        Assert.Empty(query.RelationshipTypes);
        Assert.True(query.WalksDependencies && query.WalksDependents);
    }

    [Fact]
    public void The_graph_reads_names_in_any_case_and_names_every_bad_parameter()
    {
        var (query, _) = GraphQuery.Parse(Query(("ci", Guid.CreateVersion7().ToString("D")), ("depth", "6"), ("direction", "dependents"),
            ("type", "virtualmachine"), ("type", "VirtualMachine"), ("relationshipType", "RUNS_ON"), ("retired", "true"), ("group", "false")), Types, RelationshipTypes);
        Assert.Equal((6, "dependents", true, false), (query!.Depth, query.Direction, query.Retired, query.Group));
        Assert.Equal(["VirtualMachine"], query.Types);
        Assert.Equal(["runs_on"], query.RelationshipTypes);
        Assert.False(query.WalksDependencies);

        var (refused, errors) = GraphQuery.Parse(Query(("ci", "esx-mx-014"), ("depth", "0"), ("direction", "up"), ("type", "Printer"),
            ("relationshipType", "hosts"), ("retired", "1"), ("group", "yes")), Types, RelationshipTypes);
        Assert.Null(refused);
        Assert.Equal(new Dictionary<string, string>
        {
            ["ci"] = "InvalidId",
            ["depth"] = "InvalidDepth",
            ["direction"] = "InvalidDirection",
            ["type"] = "UnknownType",
            ["relationshipType"] = "UnknownRelationshipType",
            ["retired"] = "InvalidBoolean",
            ["group"] = "InvalidBoolean",
        }, errors.ToDictionary(e => e.Key, e => Assert.Single(e.Value)));
        Assert.Equal("Required", Assert.Single(GraphQuery.Parse(Query(), Types, RelationshipTypes).Errors["ci"]));
        Assert.Equal("InvalidDepth", Assert.Single(GraphQuery.Parse(Query(("ci", Guid.Empty.ToString("D")), ("depth", "7")), Types, RelationshipTypes).Errors["depth"]));
    }

    [Theory]
    [InlineData("app-erp-db01", "app-erp-db01")]
    [InlineData("SQLPRD01\\ERP", "SQLPRD01-ERP")]
    [InlineData("ERP Reporting (old)", "ERP-Reporting-old")]
    [InlineData("Zürich-01", "Z-rich-01")]
    [InlineData("…", "ci")]
    public void The_export_file_names_the_CI_in_letters_digits_and_hyphens(string name, string file) =>
        Assert.Equal(file, GraphEndpoints.FileName(name));

    [Fact]
    public void The_export_lists_the_table_views_rows_by_hops_with_formula_cells_neutralised()
    {
        var (vm, host, sw, svc) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var result = new GraphResult(vm, 3, "both",
            [
                new GraphNode(vm, "app-erp-db01", "VirtualMachine", "Active", 0, false),
                new GraphNode(host, "esx-mx-014", "HypervisorHost", "Active", 1, false),
                new GraphNode(sw, "=HYPERLINK(\"x\")", "NetworkDevice", "Active", 2, false),
                new GraphNode(svc, "ERP, finance", "BusinessService", "Active", 1, false),
            ],
            [
                new GraphRelationship(Guid.CreateVersion7(), "uplinks_to", host, sw, "vSwitch0", 2, null),
                new GraphRelationship(Guid.CreateVersion7(), "runs_on", vm, host, null, 1, null),
                new GraphRelationship(Guid.CreateVersion7(), "depends_on", svc, vm, null, 1, null),
            ],
            [], [1, 2, 1], Truncated: false);

        Assert.Equal(
            "From,From type,Relationship,To,To type,Redundancy group,Hops\r\n"
            + "app-erp-db01,VirtualMachine,runs_on,esx-mx-014,HypervisorHost,,1\r\n"
            + "\"ERP, finance\",BusinessService,depends_on,app-erp-db01,VirtualMachine,,1\r\n"
            + "esx-mx-014,HypervisorHost,uplinks_to,\"'=HYPERLINK(\"\"x\"\")\",NetworkDevice,vSwitch0,2\r\n",
            GraphEndpoints.ToCsv(result));
    }

    [Fact]
    public void The_audit_filter_finds_the_entries_about_a_relationship()
    {
        var id = Guid.CreateVersion7();

        var filter = AuditTargetFilter.Parse($"relationship:{id:D}");

        Assert.Equal(new AuditTargetFilter(AuditTargetTypes.Relationship, id, null), filter);
        Assert.Equal($"relationship:{id:D}", filter!.ToString());
        Assert.Null(AuditTargetFilter.Parse("relationship:12"));
    }

    private static readonly IReadOnlyList<CiTypeInfo> Types =
        [new(8, "VirtualMachine", "Compute", "virtual-machine", true, []), new(6, "HypervisorHost", "Compute", "hypervisor-host", true, [])];

    private static readonly IReadOnlyList<RelationshipTypeInfo> RelationshipTypes =
        [new(1, "runs_on", "VirtualMachine", "HypervisorHost", "Down", true, null, "", null, null)];

    private static QueryCollection Query(params (string Key, string Value)[] values) =>
        new(values.GroupBy(v => v.Key).ToDictionary(g => g.Key, g => new StringValues([.. g.Select(v => v.Value)])));
}

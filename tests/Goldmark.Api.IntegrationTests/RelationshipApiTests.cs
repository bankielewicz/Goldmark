using System.Net;
using System.Text.Json;
using Goldmark.Api.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Goldmark.Api.IntegrationTests;

/// <summary>
/// Screen 5's Relationships tab and screen 6's graph (US-11, US-40, FR-CM-6; DEC-046) against SQL Server 2025 with
/// real sessions: an Editor's add, change and delete with If-Match, each audited in its transaction; the checks on the
/// ends; the tab's sections and paging; the redundancy group rule; the lock that makes an add and a retire wait for
/// each other; the graph's walks, redundancy members, filters, groups and limit; and the audited CSV export. The 403s
/// for a Viewer are in the M6 sweep (<see cref="AccountAndAccessTests"/>).
/// </summary>
[Collection(SqlServerTests.Name)]
public sealed class RelationshipApiTests(SqlServerFixture sql)
{
    [Fact]
    public async Task An_Editor_adds_changes_and_deletes_a_relationship_each_audited_and_the_history_keeps_it()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, _) = await scenario.SetUpAsync();
        var (editor, editorPerson) = await scenario.InviteAndAcceptAsync(admin, "eddie.editor", "Editor");
        var (viewer, _) = await scenario.InviteAndAcceptAsync(admin, "vic.viewer", "Viewer");
        var vm = await CiAsync(editor, "VirtualMachine", "app-erp-db01");
        var host = await CiAsync(editor, "HypervisorHost", "esx-mx-014");

        var added = await editor.PostAsync("/api/v1/relationships", new { type = "runs_on", from = vm, to = host });
        Assert.True(added.Status == HttpStatusCode.Created, added.ToString());
        var id = added.Value("id");
        Assert.Equal($"/api/v1/relationships/{id}", added.Headers["Location"]);
        Assert.Equal($"\"{added.Value("eTag")}\"", added.ETag);
        Assert.Equal(("runs_on", "app-erp-db01", "VirtualMachine", "esx-mx-014", "HypervisorHost"), (added.Value("type"),
            added.Body.GetProperty("from").Value("name"), added.Body.GetProperty("from").Value("type"), added.Body.GetProperty("to").Value("name"),
            added.Body.GetProperty("to").Value("type")));
        Assert.Equal(JsonValueKind.Null, added.Body.GetProperty("redundancyGroup").ValueKind);
        Assert.Equal(JsonValueKind.String, added.Body.GetProperty("createdAt").ValueKind);
        Assert.Equal(editorPerson.Id.ToString("D"), added.Body.GetProperty("changedBy").Value("id"));
        Assert.Equal([$"{id}|User|{editorPerson.Id:D}|{{\"type\":\"runs_on\",\"from\":\"app-erp-db01\",\"fromId\":\"{vm}\",\"fromType\":\"VirtualMachine\","
            + $"\"to\":\"esx-mx-014\",\"toId\":\"{host}\",\"toType\":\"HypervisorHost\",\"redundancyGroup\":null}}"], await scenario.AuditAsync("relationship.added"));

        // Each CI's tab lists it in its own section; everyone signed in reads it.
        var vmTab = AuthScenario.Ok(await viewer.GetAsync($"/api/v1/cis/{vm}/relationships")).Body;
        Assert.Equal((1, 0), (vmTab.GetProperty("dependencies").GetProperty("total").GetInt32(), vmTab.GetProperty("dependents").GetProperty("total").GetInt32()));
        Assert.Equal(id, vmTab.GetProperty("dependencies").GetProperty("items")[0].Value("id"));
        var hostTab = AuthScenario.Ok(await viewer.GetAsync($"/api/v1/cis/{host}/relationships")).Body;
        Assert.Equal(id, hostTab.GetProperty("dependents").GetProperty("items")[0].Value("id"));

        // A change needs the version: 428 without it, 412 with an old one. The group is trimmed and stored as typed.
        var path = $"/api/v1/relationships/{id}";
        Assert.Equal(HttpStatusCode.PreconditionRequired, (await editor.PutAsync(path, new { redundancyGroup = "AG-ERP" })).Status);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await editor.PutAsync(path, new { redundancyGroup = "AG-ERP" }, "\"2001-01-01T00:00:00.0000000\"")).Status);
        var changed = await editor.PutAsync(path, new { redundancyGroup = "  AG-ERP  " }, added.ETag);
        Assert.True(changed.Status == HttpStatusCode.OK, changed.ToString());
        Assert.Equal("AG-ERP", changed.Value("redundancyGroup"));
        Assert.Equal(added.Value("createdAt"), changed.Value("createdAt"));
        Assert.NotEqual(added.ETag, changed.ETag);
        var again = await editor.PutAsync(path, new { redundancyGroup = "AG-ERP" }, changed.ETag);
        Assert.Equal(changed.ETag, again.ETag); // nothing changed, nothing written
        var changes = await scenario.AuditAsync("relationship.changed");
        Assert.EndsWith(",\"changed\":[\"RedundancyGroup\"],\"before\":null,\"after\":\"AG-ERP\"}", Assert.Single(changes), StringComparison.Ordinal);

        // A delete needs the version too; the row goes and its history keeps every version (DEC-046 item 1).
        Assert.Equal(HttpStatusCode.PreconditionRequired, (await editor.DeleteAsync(path)).Status);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await editor.SendAsync(HttpMethod.Delete, path, ifMatch: added.ETag)).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await editor.SendAsync(HttpMethod.Delete, path, ifMatch: changed.ETag)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync(path)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await editor.SendAsync(HttpMethod.Delete, path, ifMatch: changed.ETag)).Status);
        Assert.Equal(2, await scenario.Sql.AdminScalarAsync<int>(scenario.Installation.Database,
            $"SELECT COUNT(*) FROM cmdb.Relationship FOR SYSTEM_TIME ALL WHERE Id = '{id}';"));
        Assert.EndsWith(",\"redundancyGroup\":\"AG-ERP\"}", Assert.Single(await scenario.AuditAsync("relationship.deleted")), StringComparison.Ordinal);

        // Screen 16 names it from what the entries recorded, and finds them all by the relationship.
        var entries = AuthScenario.Ok(await admin.GetAsync($"/api/v1/audit?target=relationship:{id}")).Body.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(["relationship.deleted", "relationship.changed", "relationship.added"], entries.Select(e => e.Value("action")));
        Assert.All(entries, e => Assert.Equal("app-erp-db01 runs_on esx-mx-014", e.GetProperty("target").Value("name")));
        Assert.All(entries, e => Assert.Equal("Relationship", e.GetProperty("target").Value("type")));
    }

    [Fact]
    public async Task The_ends_are_checked_against_the_type_and_a_refused_relationship_writes_nothing()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, _) = await scenario.SetUpAsync();
        var (editor, _) = await scenario.InviteAndAcceptAsync(admin, "eddie.editor", "Editor");
        var vm = await CiAsync(editor, "VirtualMachine", "app-erp-db01");
        var host = await CiAsync(editor, "HypervisorHost", "esx-mx-014");
        var server = await CiAsync(editor, "Server", "APP-ERP-DB01");
        var team = await CiAsync(editor, "Team", "ERP Platform");
        var old = await CiAsync(editor, "BusinessService", "Payroll");
        await RetireAsync(editor, old);

        Assert.Equal("Required", Field(await editor.PostAsync("/api/v1/relationships", new { from = vm, to = host }), "type"));
        Assert.Equal("UnknownRelationshipType", Field(await editor.PostAsync("/api/v1/relationships", new { type = "hosts", from = vm, to = host }), "type"));
        Assert.Equal("Required", Field(await editor.PostAsync("/api/v1/relationships", new { type = "runs_on", to = host }), "from"));
        Assert.Equal("CiNotFound", Field(await editor.PostAsync("/api/v1/relationships", new { type = "runs_on", from = Guid.NewGuid().ToString("D"), to = host }), "from"));
        Assert.Equal("CiNotFound", Field(await editor.PostAsync("/api/v1/relationships", new { type = "runs_on", from = vm, to = "esx-mx-014" }), "to"));
        Assert.Equal("Self", Field(await editor.PostAsync("/api/v1/relationships", new { type = "depends_on", from = vm, to = vm }), "to"));
        Assert.Equal("WrongType", Field(await editor.PostAsync("/api/v1/relationships", new { type = "runs_on", from = server, to = host }), "from"));
        Assert.Equal("WrongType", Field(await editor.PostAsync("/api/v1/relationships", new { type = "runs_on", from = vm, to = server }), "to"));
        Assert.Equal("WrongType", Field(await editor.PostAsync("/api/v1/relationships", new { type = "owned_by", from = vm, to = server }), "to"));
        Assert.Equal("CiRetired", Field(await editor.PostAsync("/api/v1/relationships", new { type = "depends_on", from = old, to = vm }), "from"));
        Assert.Equal("TooLong", Field(await editor.PostAsync("/api/v1/relationships",
            new { type = "runs_on", from = vm, to = host, redundancyGroup = new string('g', 201) }), "redundancyGroup"));
        Assert.Equal("InvalidText", Field(await editor.PostAsync("/api/v1/relationships",
            new { type = "runs_on", from = vm, to = host, redundancyGroup = "vSwitch0\nvSwitch1" }), "redundancyGroup"));
        Assert.Empty(await scenario.AuditAsync("relationship.added"));

        // owned_by's dependency is a Team or a Person; depends_on takes any two CIs; any case finds the type.
        Assert.Equal(HttpStatusCode.Created, (await editor.PostAsync("/api/v1/relationships", new { type = "owned_by", from = vm, to = team })).Status);
        Assert.Equal(HttpStatusCode.Created, (await editor.PostAsync("/api/v1/relationships", new { type = "Depends_On", from = server, to = team })).Status);
        var withGroup = await editor.PostAsync("/api/v1/relationships", new { type = "runs_on", from = vm, to = host, redundancyGroup = new string('g', 200) });
        Assert.True(withGroup.Status == HttpStatusCode.Created, withGroup.ToString());

        // The same from, type and to again is refused; the reverse direction is another relationship.
        var exists = await editor.PostAsync("/api/v1/relationships", new { type = "runs_on", from = vm, to = host });
        Assert.Equal((HttpStatusCode.Conflict, "exists"), (exists.Status, exists.Code));
        Assert.Equal(3, (await scenario.AuditAsync("relationship.added")).Count);
    }

    [Fact]
    public async Task A_relationship_with_a_retired_CI_is_still_listed_changed_and_deleted()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, _) = await scenario.SetUpAsync();
        var (editor, _) = await scenario.InviteAndAcceptAsync(admin, "eddie.editor", "Editor");
        var vm = await CiAsync(editor, "VirtualMachine", "app-erp-db01");
        var service = await CiAsync(editor, "BusinessService", "Payroll");
        var added = await editor.PostAsync("/api/v1/relationships", new { type = "depends_on", from = service, to = vm });
        await RetireAsync(editor, service);

        var listed = AuthScenario.Ok(await editor.GetAsync($"/api/v1/cis/{vm}/relationships")).Body.GetProperty("dependents").GetProperty("items")[0];
        Assert.Equal("Retired", listed.GetProperty("from").Value("lifecycleState"));
        var changed = await editor.PutAsync($"/api/v1/relationships/{added.Value("id")}", new { redundancyGroup = "old payroll" }, added.ETag);
        Assert.True(changed.Status == HttpStatusCode.OK, changed.ToString());
        Assert.Equal(HttpStatusCode.NoContent, (await editor.SendAsync(HttpMethod.Delete, $"/api/v1/relationships/{added.Value("id")}", ifMatch: changed.ETag)).Status);
    }

    [Fact]
    public async Task Each_section_of_the_tab_is_sorted_by_type_then_name_and_paged()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, _) = await scenario.SetUpAsync();
        var (editor, _) = await scenario.InviteAndAcceptAsync(admin, "eddie.editor", "Editor");
        var host = await CiAsync(editor, "HypervisorHost", "esx-mx-014");
        var cluster = await CiAsync(editor, "HypervisorCluster", "CL-PROD-02");
        await RelateAsync(editor, "member_of_cluster", host, cluster);
        await SeedVmsOnAsync(scenario, host, "vm-", 60);
        var service = await CiAsync(editor, "BusinessService", "Billing");
        await RelateAsync(editor, "depends_on", service, host);

        var first = AuthScenario.Ok(await editor.GetAsync($"/api/v1/cis/{host}/relationships")).Body;
        var dependents = first.GetProperty("dependents");
        Assert.Equal(61, dependents.GetProperty("total").GetInt32());
        var items = dependents.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(50, items.Count);
        Assert.Equal(("depends_on", "Billing"), (items[0].Value("type"), items[0].GetProperty("from").Value("name")));
        Assert.Equal(("runs_on", "vm-001"), (items[1].Value("type"), items[1].GetProperty("from").Value("name")));
        Assert.Equal("CL-PROD-02", first.GetProperty("dependencies").GetProperty("items")[0].GetProperty("to").Value("name"));

        var next = dependents.Value("next");
        var rest = AuthScenario.Ok(await editor.GetAsync($"/api/v1/cis/{host}/relationships?direction=dependents&limit=200&cursor={next}")).Body;
        Assert.Equal(11, rest.GetProperty("items").GetArrayLength());
        Assert.Equal("vm-060", rest.GetProperty("items")[10].GetProperty("from").Value("name"));
        Assert.Equal(JsonValueKind.Null, rest.GetProperty("next").ValueKind);
        Assert.Equal(JsonValueKind.Null, rest.GetProperty("total").ValueKind);

        Assert.Equal("InvalidCursor", Field(await editor.GetAsync($"/api/v1/cis/{host}/relationships?cursor={next}"), "cursor"));
        Assert.Equal("InvalidCursor", Field(await editor.GetAsync($"/api/v1/cis/{host}/relationships?direction=dependencies&cursor={next}"), "cursor"));
        Assert.Equal("InvalidLimit", Field(await editor.GetAsync($"/api/v1/cis/{host}/relationships?limit=201"), "limit"));
        Assert.Equal("InvalidDirection", Field(await editor.GetAsync($"/api/v1/cis/{host}/relationships?direction=both"), "direction"));
        Assert.Equal(HttpStatusCode.NotFound, (await editor.GetAsync($"/api/v1/cis/{Guid.NewGuid():D}/relationships")).Status);
    }

    [Fact]
    public async Task Group_texts_that_differ_only_in_case_are_one_group_of_the_dependents_relationships()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, _) = await scenario.SetUpAsync();
        var (editor, _) = await scenario.InviteAndAcceptAsync(admin, "eddie.editor", "Editor");
        var host = await CiAsync(editor, "HypervisorHost", "esx-mx-014");
        foreach (var (name, group) in new[] { ("sw-core-01", "vSwitch0"), ("sw-core-02", "vswitch0"), ("sw-core-03", "vSwitch0"), ("sw-core-04", "vSwitch1"), ("sw-core-05", "") })
        {
            await RelateAsync(editor, "uplinks_to", host, await CiAsync(editor, "NetworkDevice", name), group);
        }

        var groups = AuthScenario.Ok(await editor.GetAsync($"/api/v1/cis/{host}/redundancy-groups")).Body.GetProperty("groups").EnumerateArray()
            .Select(g => $"{g.Value("name")}:{g.GetProperty("relationships").GetInt32()}");
        Assert.Equal(["vSwitch0:3", "vSwitch1:1"], groups);
        Assert.Empty(AuthScenario.Ok(await editor.GetAsync($"/api/v1/cis/{await CiAsync(editor, "NetworkDevice", "sw-core-06")}/redundancy-groups")).Body
            .GetProperty("groups").EnumerateArray());
    }

    [Fact]
    public async Task An_add_holds_both_CIs_so_a_retire_in_progress_finishes_first_and_the_add_sees_it()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, _) = await scenario.SetUpAsync();
        var (editor, _) = await scenario.InviteAndAcceptAsync(admin, "eddie.editor", "Editor");
        var vm = await CiAsync(editor, "VirtualMachine", "app-erp-db01");
        var host = await CiAsync(editor, "HypervisorHost", "esx-mx-014");

        // Another session retires the host and hasn't committed; the add waits for it at the host's row, after the
        // relationship type's and both CI types' locks, and then finds the host retired.
        await using var other = await scenario.Sql.OpenAsync(SqlServerFixture.AdminUser, scenario.Sql.AdminPassword, scenario.Installation.Database);
        await using var transaction = (SqlTransaction)await other.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var session = await ExecuteAsync<short>(other, transaction, "SELECT @@SPID;");
        await ExecuteAsync<object>(other, transaction, $"UPDATE cmdb.Ci SET LifecycleState = 'Retired' WHERE Id = '{host}';");
        var add = editor.PostAsync("/api/v1/relationships", new { type = "runs_on", from = vm, to = host });
        for (var wait = 0; await scenario.Sql.AdminScalarAsync<int>(scenario.Installation.Database,
            $"SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id = {session};") == 0; wait++)
        {
            Assert.True(wait < 300, "The add never waited for the retire.");
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        await transaction.CommitAsync(TestContext.Current.CancellationToken);

        Assert.Equal("CiRetired", Field(await add, "to"));
        Assert.Empty(await scenario.AuditAsync("relationship.added"));
    }

    [Fact]
    public async Task The_graph_walks_both_ways_and_adds_redundancy_group_members_at_their_true_hop()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, _) = await scenario.SetUpAsync();
        var (editor, _) = await scenario.InviteAndAcceptAsync(admin, "eddie.editor", "Editor");
        var (viewer, _) = await scenario.InviteAndAcceptAsync(admin, "vic.viewer", "Viewer");
        var d1 = await SampleAsync(editor);

        // The boards' sample (DEC-046 item 4): 14 CIs and 13 relationships at depth 3, Payroll (retired) left out,
        // SQLPRD02\ERP at hop 4 as AG-ERP's other replica.
        var graph = AuthScenario.Ok(await viewer.GetAsync($"/api/v1/graph?ci={d1["app-erp-db01"]}")).Body;
        Assert.Equal((3, "both", false), (graph.GetProperty("depth").GetInt32(), graph.Value("direction"), graph.GetProperty("truncated").GetBoolean()));
        Assert.Equal(new Dictionary<string, int>
        {
            ["app-erp-db01"] = 0,
            ["APP-ERP-DB01"] = 1,
            ["ERP"] = 1,
            ["ERP Reporting"] = 1,
            ["esx-mx-014"] = 1,
            ["DS-S3-ERP-01"] = 1,
            ["SQLPRD01\\ERP"] = 2,
            ["S3"] = 2,
            ["CL-PROD-02"] = 2,
            ["sw-core-01"] = 2,
            ["sw-core-02"] = 2,
            ["AG-ERP"] = 3,
            ["FS7300-A"] = 3,
            ["SQLPRD02\\ERP"] = 4,
        }, Hops(graph));
        var relationships = graph.GetProperty("relationships").EnumerateArray().ToList();
        Assert.Equal(13, relationships.Count);
        var second = Assert.Single(relationships, r => r.Value("to") == d1["SQLPRD02\\ERP"]);
        Assert.Equal(("replica_on", "ag-erp", 4), (second.Value("type"), second.Value("redundancyGroup"), second.GetProperty("hops").GetInt32()));
        Assert.Equal([1, 5, 5, 2, 1], graph.GetProperty("countsByHop").EnumerateArray().Select(c => c.GetInt32()));
        Assert.Empty(graph.GetProperty("groups").EnumerateArray());

        // Retired CIs when asked; one direction; a shallower depth (no AG, so no second replica); the filters stop the walk.
        Assert.Equal(15, Hops(AuthScenario.Ok(await viewer.GetAsync($"/api/v1/graph?ci={d1["app-erp-db01"]}&retired=true")).Body).Count);
        Assert.Equal(["app-erp-db01", "CL-PROD-02", "DS-S3-ERP-01", "esx-mx-014", "FS7300-A", "S3", "sw-core-01", "sw-core-02"],
            Hops(AuthScenario.Ok(await viewer.GetAsync($"/api/v1/graph?ci={d1["app-erp-db01"]}&direction=dependencies")).Body).Keys
                .Order(StringComparer.OrdinalIgnoreCase));
        var depth2 = Hops(AuthScenario.Ok(await viewer.GetAsync($"/api/v1/graph?ci={d1["app-erp-db01"]}&depth=2")).Body);
        Assert.False(depth2.ContainsKey("AG-ERP") || depth2.ContainsKey("SQLPRD02\\ERP"));
        Assert.Equal(["ERP", "ERP Reporting", "app-erp-db01"], Hops(AuthScenario.Ok(await viewer.GetAsync(
            $"/api/v1/graph?ci={d1["app-erp-db01"]}&type=BusinessService")).Body).Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["app-erp-db01", "esx-mx-014"], Hops(AuthScenario.Ok(await viewer.GetAsync(
            $"/api/v1/graph?ci={d1["app-erp-db01"]}&relationshipType=runs_on&relationshipType=RUNS_ON")).Body).Keys.Order(StringComparer.Ordinal));

        // From the AG's other side: the dependents walk from SQLPRD02\ERP reaches AG-ERP through the group, so
        // SQLPRD01\ERP comes in at hop 2.
        Assert.Equal(new Dictionary<string, int> { ["SQLPRD02\\ERP"] = 0, ["AG-ERP"] = 1, ["SQLPRD01\\ERP"] = 2 },
            Hops(AuthScenario.Ok(await viewer.GetAsync($"/api/v1/graph?ci={d1["SQLPRD02\\ERP"]}&depth=1")).Body));

        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/v1/graph?ci={Guid.NewGuid():D}")).Status);
        var invalid = await viewer.GetAsync("/api/v1/graph?depth=7&direction=up&type=Printer&relationshipType=hosts&retired=yes&group=no");
        Assert.Equal(["ci", "depth", "direction", "group", "relationshipType", "retired", "type"],
            invalid.Body.GetProperty("errors").EnumerateObject().Select(e => e.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task More_than_ten_neighbours_of_one_type_become_a_group_and_the_walk_stops_there()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, _) = await scenario.SetUpAsync();
        var (editor, _) = await scenario.InviteAndAcceptAsync(admin, "eddie.editor", "Editor");
        var host = await CiAsync(editor, "HypervisorHost", "esx-mx-014");
        var small = await CiAsync(editor, "HypervisorHost", "esx-mx-015");
        await RelateAsync(editor, "member_of_cluster", host, await CiAsync(editor, "HypervisorCluster", "CL-PROD-02"));
        await SeedVmsOnAsync(scenario, host, "vm-", 12);
        await SeedVmsOnAsync(scenario, small, "small-", 10);
        // Each of the host's VMs has a service that depends on it, which the walk never reaches past the group.
        await scenario.Sql.AsAdminAsync(scenario.Installation.Database, """
            INSERT INTO cmdb.Ci (Id, TenantId, CiTypeId, Name, LifecycleState, Attributes, ChangedBy)
            SELECT NEWID(), '00000000-0000-0000-0000-000000000001', (SELECT Id FROM cmdb.CiType WHERE Name = N'BusinessService'), CONCAT(N'svc-', v.Name), 'Active', '{}', 'test'
            FROM cmdb.Ci AS v WHERE v.Name LIKE N'vm-%';
            INSERT INTO cmdb.Relationship (Id, TenantId, FromCiId, ToCiId, RelationshipTypeId, ChangedBy)
            SELECT NEWID(), '00000000-0000-0000-0000-000000000001', s.Id, v.Id, (SELECT Id FROM cmdb.RelationshipType WHERE Name = N'depends_on'), 'test'
            FROM cmdb.Ci AS s JOIN cmdb.Ci AS v ON s.Name = CONCAT(N'svc-', v.Name);
            """);

        var grouped = AuthScenario.Ok(await editor.GetAsync($"/api/v1/graph?ci={host}")).Body;
        var group = Assert.Single(grouped.GetProperty("groups").EnumerateArray());
        Assert.Equal((host, "dependents", "runs_on", "VirtualMachine", 1, 12), (group.Value("hub"), group.Value("direction"), group.Value("relationshipType"),
            group.Value("ciType"), group.GetProperty("hops").GetInt32(), group.GetProperty("count").GetInt32()));
        Assert.Equal(12, group.GetProperty("members").GetArrayLength());
        var nodes = grouped.GetProperty("nodes").EnumerateArray().ToList();
        Assert.Equal(14, nodes.Count); // the host, its cluster and the 12 VMs, no service
        Assert.Equal(12, nodes.Count(n => n.GetProperty("grouped").GetBoolean()));
        Assert.All(grouped.GetProperty("relationships").EnumerateArray().Where(r => r.Value("type") == "runs_on"), r => Assert.Equal(group.Value("id"), r.Value("group")));
        Assert.Equal([1, 2], grouped.GetProperty("countsByHop").EnumerateArray().Select(c => c.GetInt32()));

        // Off, the walk goes through the VMs to their services; ten neighbours are never a group.
        var flat = AuthScenario.Ok(await editor.GetAsync($"/api/v1/graph?ci={host}&group=false")).Body;
        Assert.Empty(flat.GetProperty("groups").EnumerateArray());
        Assert.Equal([1, 13, 12], flat.GetProperty("countsByHop").EnumerateArray().Select(c => c.GetInt32()));
        Assert.Empty(AuthScenario.Ok(await editor.GetAsync($"/api/v1/graph?ci={small}")).Body.GetProperty("groups").EnumerateArray());
    }

    [Fact]
    public async Task The_graph_export_is_a_CSV_of_the_tables_rows_recorded_in_the_audit_log()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, _) = await scenario.SetUpAsync();
        var (editor, _) = await scenario.InviteAndAcceptAsync(admin, "eddie.editor", "Editor");
        var (viewer, viewerPerson) = await scenario.InviteAndAcceptAsync(admin, "vic.viewer", "Viewer");
        var d1 = await SampleAsync(editor);
        var vm = d1["app-erp-db01"];

        var export = await viewer.PostAsync($"/api/v1/graph/export?ci={vm}&depth=3");
        Assert.True(export.Status == HttpStatusCode.OK, export.ToString());
        Assert.Equal("text/csv", export.ContentType);
        Assert.Equal("13", export.Headers["X-Goldmark-Entries"]);
        var lines = export.Text.TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("From,From type,Relationship,To,To type,Redundancy group,Hops", lines[0]);
        Assert.Equal(14, lines.Length);
        Assert.Equal("AG-ERP,SqlAvailabilityGroup,replica_on,SQLPRD02\\ERP,SqlInstance,ag-erp,4", lines[^1]);
        Assert.Equal([$"{vm}|User|{viewerPerson.Id:D}|{{\"name\":\"app-erp-db01\",\"type\":\"VirtualMachine\",\"filters\":{{\"depth\":3,\"direction\":\"both\","
            + "\"types\":[],\"relationshipTypes\":[],\"retired\":false,\"group\":true},\"relationships\":13}"], await scenario.AuditAsync("relationship.exported"));

        // A bad parameter or an unknown CI records nothing; the export is a POST, so it needs the anti-forgery header.
        Assert.Equal(HttpStatusCode.BadRequest, (await viewer.PostAsync($"/api/v1/graph/export?ci={vm}&depth=0")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.PostAsync($"/api/v1/graph/export?ci={Guid.NewGuid():D}")).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await viewer.SendAsync(HttpMethod.Post, $"/api/v1/graph/export?ci={vm}", new { }, antiforgery: false)).Status);
        Assert.Single(await scenario.AuditAsync("relationship.exported"));
    }

    [Fact]
    public async Task A_walk_past_the_limit_stops_before_the_hop_and_its_export_is_refused()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, _) = await scenario.SetUpAsync();
        var (editor, _) = await scenario.InviteAndAcceptAsync(admin, "eddie.editor", "Editor");
        var host = await CiAsync(editor, "HypervisorHost", "esx-big");
        await SeedVmsOnAsync(scenario, host, "vm-", 5_001);

        var graph = AuthScenario.Ok(await editor.GetAsync($"/api/v1/graph?ci={host}&group=false")).Body;
        Assert.True(graph.GetProperty("truncated").GetBoolean());
        Assert.Equal(1, graph.GetProperty("nodes").GetArrayLength());
        Assert.Equal([1], graph.GetProperty("countsByHop").EnumerateArray().Select(c => c.GetInt32()));

        // Grouped, the hop is still read whole before it is grouped, so the limit holds the same.
        Assert.True(AuthScenario.Ok(await editor.GetAsync($"/api/v1/graph?ci={host}")).Body.GetProperty("truncated").GetBoolean());

        var refused = await editor.PostAsync($"/api/v1/graph/export?ci={host}&group=false");
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "export-too-large"), (refused.Status, refused.Code));
        Assert.Equal(5000, refused.Body.GetProperty("limit").GetInt32());
        Assert.Empty(await scenario.AuditAsync("relationship.exported"));
    }

    /// <summary>The boards' sample around app-erp-db01 (Graph.dc.html), by name.</summary>
    private static async Task<Dictionary<string, string>> SampleAsync(ApiClient editor)
    {
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (type, name) in new[]
        {
            ("VirtualMachine", "app-erp-db01"), ("Server", "APP-ERP-DB01"), ("SqlInstance", "SQLPRD01\\ERP"), ("SqlInstance", "SQLPRD02\\ERP"),
            ("SqlAvailabilityGroup", "AG-ERP"), ("BusinessService", "ERP"), ("BusinessService", "ERP Reporting"), ("BusinessService", "Payroll"),
            ("HypervisorHost", "esx-mx-014"), ("Datastore", "DS-S3-ERP-01"), ("StorageGroup", "S3"), ("StorageArray", "FS7300-A"),
            ("HypervisorCluster", "CL-PROD-02"), ("NetworkDevice", "sw-core-01"), ("NetworkDevice", "sw-core-02"),
        })
        {
            ids[name] = await CiAsync(editor, type, name);
        }

        foreach (var (from, type, to, group) in new[]
        {
            ("APP-ERP-DB01", "os_of", "app-erp-db01", ""), ("app-erp-db01", "runs_on", "esx-mx-014", ""),
            ("app-erp-db01", "uses_datastore", "DS-S3-ERP-01", ""), ("ERP", "depends_on", "app-erp-db01", ""),
            ("ERP Reporting", "depends_on", "app-erp-db01", ""), ("Payroll", "depends_on", "app-erp-db01", ""),
            ("DS-S3-ERP-01", "backed_by", "S3", ""), ("esx-mx-014", "member_of_cluster", "CL-PROD-02", ""),
            ("esx-mx-014", "uplinks_to", "sw-core-01", "vSwitch0"), ("esx-mx-014", "uplinks_to", "sw-core-02", "vSwitch0"),
            ("SQLPRD01\\ERP", "hosted_on", "APP-ERP-DB01", ""), ("AG-ERP", "replica_on", "SQLPRD01\\ERP", "AG-ERP"),
            ("S3", "part_of_array", "FS7300-A", ""), ("AG-ERP", "replica_on", "SQLPRD02\\ERP", "ag-erp"),
        })
        {
            await RelateAsync(editor, type, ids[from], ids[to], group);
        }

        await RetireAsync(editor, ids["Payroll"]);
        return ids;
    }

    /// <summary>Many VMs that run on a host, written in SQL (the API would take minutes).</summary>
    private static Task SeedVmsOnAsync(AuthScenario scenario, string host, string prefix, int count) =>
        scenario.Sql.AsAdminAsync(scenario.Installation.Database, $$"""
            WITH n AS (SELECT TOP ({{count}}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i FROM sys.all_objects AS a CROSS JOIN sys.all_objects AS b)
            INSERT INTO cmdb.Ci (Id, TenantId, CiTypeId, Name, LifecycleState, Attributes, ChangedBy)
            SELECT NEWID(), '00000000-0000-0000-0000-000000000001', (SELECT Id FROM cmdb.CiType WHERE Name = N'VirtualMachine'),
                CONCAT(N'{{prefix}}', FORMAT(i, '000')), 'Active', '{}', 'test'
            FROM n;
            INSERT INTO cmdb.Relationship (Id, TenantId, FromCiId, ToCiId, RelationshipTypeId, ChangedBy)
            SELECT NEWID(), '00000000-0000-0000-0000-000000000001', v.Id, '{{host}}', (SELECT Id FROM cmdb.RelationshipType WHERE Name = N'runs_on'), 'test'
            FROM cmdb.Ci AS v WHERE v.Name LIKE N'{{prefix}}%';
            """);

    private static Dictionary<string, int> Hops(JsonElement graph) =>
        graph.GetProperty("nodes").EnumerateArray().ToDictionary(n => n.Value("name"), n => n.GetProperty("hops").GetInt32(), StringComparer.Ordinal);

    private static async Task<string> CiAsync(ApiClient editor, string type, string name)
    {
        var response = await editor.PostAsync("/api/v1/cis", new { type, name });
        Assert.True(response.Status == HttpStatusCode.Created, response.ToString());
        return response.Value("id");
    }

    private static async Task RelateAsync(ApiClient editor, string type, string from, string to, string group = "")
    {
        var response = await editor.PostAsync("/api/v1/relationships", new { type, from, to, redundancyGroup = group });
        Assert.True(response.Status == HttpStatusCode.Created, response.ToString());
    }

    private static async Task RetireAsync(ApiClient editor, string id)
    {
        var ci = AuthScenario.Ok(await editor.GetAsync($"/api/v1/cis/{id}"));
        AuthScenario.Ok(await editor.SendAsync(HttpMethod.Post, $"/api/v1/cis/{id}/retire", new { }, ifMatch: ci.ETag));
    }

    private static string? Field(Response response, string field)
    {
        Assert.True(response.Status == HttpStatusCode.BadRequest, response.ToString());
        return response.Body.GetProperty("errors").GetProperty(field)[0].GetString();
    }

    private static async Task<T?> ExecuteAsync<T>(SqlConnection connection, SqlTransaction transaction, string text)
    {
        await using var command = new SqlCommand(text, connection, transaction);
        return (T?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }
}

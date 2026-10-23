using System.Net;
using System.Text;
using System.Text.Json;
using Goldmark.Api.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Goldmark.Api.IntegrationTests;

/// <summary>
/// Screens 4 and 5's API (US-10, US-11, US-53; DEC-044) against SQL Server 2025 with real sessions: the
/// built-in types, an Admin's custom attributes of every kind, an Editor's create, change, retire and
/// restore with If-Match, each audited in its transaction, the checks on values and links, and the
/// search by name in any case with its filters, counts, orders and cursor. The 403s for a Viewer are in
/// the M6 sweep (<see cref="AccountAndAccessTests"/>).
/// </summary>
[Collection(SqlServerTests.Name)]
public sealed class CiApiTests(SqlServerFixture sql)
{
    private static readonly string[] RepeatedChoice = ["Gold", "Gold"];
    private static readonly string[] TickedTypes = ["VirtualMachine", "Server"];

    [Fact]
    public async Task The_built_in_types_and_relationship_types_are_there_with_only_the_attributes_the_PRD_names()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, _) = await scenario.SetUpAsync();
        var (viewer, _) = await scenario.InviteAndAcceptAsync(admin, "vic.viewer", "Viewer");

        var body = AuthScenario.Ok(await viewer.GetAsync("/api/v1/types")).Body;
        var types = body.GetProperty("types").EnumerateArray().ToList();
        Assert.Equal(
        [
            "Location", "Team", "Person", "HypervisorManager", "HypervisorCluster", "HypervisorHost", "Chassis", "VirtualMachine", "Server",
            "Appliance", "NetworkDevice", "Datastore", "StorageGroup", "StorageArray", "WindowsCluster", "ClusterNetworkName", "SqlInstance",
            "SqlAvailabilityGroup", "SqlDatabase", "BusinessService",
        ], types.Select(t => t.GetProperty("name").GetString()));
        Assert.All(types, t => Assert.True(t.GetProperty("builtIn").GetBoolean()));
        Assert.Equal("Clustering & SQL", Type(types, "SqlInstance").GetProperty("category").GetString());

        var attributes = types.SelectMany(t => t.GetProperty("attributes").EnumerateArray()
            .Select(a => $"{t.GetProperty("name").GetString()}.{a.GetProperty("name").GetString()}:{a.GetProperty("dataType").GetString()}")).ToList();
        Assert.Equal(["HypervisorHost.virtual:bool", "Server.platform:enum", "Appliance.kind:enum", "NetworkDevice.role:enum", "StorageArray.kind:enum",
            "StorageArray.family:string"], attributes);
        var platform = Type(types, "Server").GetProperty("attributes")[0];
        Assert.Equal(["physical", "vmware", "hyperv", "xen", "cloud", "other"], AuthScenario.Strings(platform.GetProperty("choices")));
        Assert.False(platform.GetProperty("required").GetBoolean());

        var relationships = body.GetProperty("relationshipTypes").EnumerateArray().ToList();
        Assert.Equal(17, relationships.Count);
        var runsOn = Assert.Single(relationships, r => r.GetProperty("name").GetString() == "runs_on");
        Assert.Equal(("VirtualMachine", "HypervisorHost", "Down"),
            (runsOn.GetProperty("from").GetString(), runsOn.GetProperty("to").GetString(), runsOn.GetProperty("impact").GetString()));
        var ownedBy = Assert.Single(relationships, r => r.GetProperty("name").GetString() == "owned_by");
        Assert.Equal(JsonValueKind.Null, ownedBy.GetProperty("to").ValueKind); // Team or Person: slice 6 checks the ends
        Assert.Equal("None", ownedBy.GetProperty("impact").GetString());
    }

    [Fact]
    public async Task An_Admin_adds_custom_attributes_of_every_kind_and_an_Editor_creates_changes_retires_and_restores_a_CI()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, adminPerson) = await scenario.SetUpAsync();
        var (editor, editorPerson) = await scenario.InviteAndAcceptAsync(admin, "eddie.editor", "Editor");
        var (viewer, _) = await scenario.InviteAndAcceptAsync(admin, "vic.viewer", "Viewer");
        await AddVmAttributesAsync(admin);

        // Only an Admin adds; adding the same name or label again is refused, and nothing else is written.
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.PostAsync("/api/v1/types/VirtualMachine/attributes",
            new { name = "owner", label = "Owner", dataType = "string" })).Status);
        Assert.Equal("DuplicateName", Field(await admin.PostAsync("/api/v1/types/VirtualMachine/attributes",
            new { name = "costCentre", label = "Cost center", dataType = "string" }), "name"));
        Assert.Equal("DuplicateLabel", Field(await admin.PostAsync("/api/v1/types/VirtualMachine/attributes",
            new { name = "costCenter", label = "cost centre", dataType = "string" }), "label"));
        Assert.Equal("InvalidChoices", Field(await admin.PostAsync("/api/v1/types/VirtualMachine/attributes",
            new { name = "tier", label = "Tier", dataType = "enum", choices = RepeatedChoice }), "choices"));
        Assert.Equal("RefTypeNotAllowed", Field(await admin.PostAsync("/api/v1/types/VirtualMachine/attributes",
            new { name = "note", label = "Note", dataType = "string", refType = "Team" }), "refType"));
        var added = await scenario.AuditAsync("type.attribute.added");
        Assert.Equal(6, added.Count);
        Assert.All(added, row => Assert.StartsWith($"8|User|{adminPerson.Id:D}|", row, StringComparison.Ordinal)); // VirtualMachine is type 8
        Assert.Contains("\"attribute\":\"Backup tier\"", added[4], StringComparison.Ordinal);

        var erp = await CreateAsync(editor, new { type = "BusinessService", name = "ERP" });
        var created = await editor.PostAsync("/api/v1/cis", new
        {
            type = "virtualmachine", // any case
            name = "  app-erp-db01 ",
            attributes = new Dictionary<string, object?>
            {
                ["costCentre"] = " CC-4410 ",
                ["vcpusReserved"] = 8,
                ["monitored"] = true,
                ["patchWindowStarts"] = "2026-11-07",
                ["backupTier"] = "Gold",
                ["supportsService"] = erp.GetProperty("id").GetString(),
            },
        });
        Assert.True(created.Status == HttpStatusCode.Created, created.ToString());
        var vm = created.Body;
        var id = vm.GetProperty("id").GetGuid();
        Assert.Equal($"/api/v1/cis/{id:D}", created.Headers["Location"]);
        Assert.Equal(("app-erp-db01", "VirtualMachine", "Active"), (vm.Value("name"), vm.Value("type"), vm.Value("lifecycleState")));
        Assert.Equal("{\"costCentre\":\"CC-4410\",\"vcpusReserved\":\"8\",\"monitored\":true,\"patchWindowStarts\":\"2026-11-07\",\"backupTier\":\"Gold\",\"supportsService\":\""
            + erp.Value("id") + "\"}", vm.GetProperty("attributes").GetRawText());
        Assert.Equal("ERP", vm.GetProperty("references").GetProperty(erp.Value("id")).Value("name"));
        Assert.Equal(editorPerson.Id, vm.GetProperty("changedBy").GetProperty("id").GetGuid());
        Assert.Equal("Eddie Editor", vm.GetProperty("changedBy").GetProperty("displayName").GetString());
        Assert.Equal($"\"{vm.Value("eTag")}\"", created.ETag);
        var creations = await scenario.AuditAsync("ci.created");
        Assert.Equal(2, creations.Count); // the service it supports, then the VM
        Assert.Equal($"{id:D}|User|{editorPerson.Id:D}|{{\"type\":\"VirtualMachine\",\"name\":\"app-erp-db01\"}}", creations[1]);

        // Everyone signed in reads it; a Viewer can't change it.
        var read = AuthScenario.Ok(await viewer.GetAsync($"/api/v1/cis/{id:D}"));
        Assert.Equal(created.ETag, read.ETag);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.SendAsync(HttpMethod.Put, $"/api/v1/cis/{id:D}", new { name = "x" }, ifMatch: read.ETag)).Status);

        // A change needs the version: none is 428, a stale one 412, and nothing is written.
        var change = new { name = "app-erp-db01", attributes = new { costCentre = "CC-4410", backupTier = "Silver", monitored = true } };
        Assert.Equal(HttpStatusCode.PreconditionRequired, (await editor.PutAsync($"/api/v1/cis/{id:D}", change)).Status);
        var changed = await editor.PutAsync($"/api/v1/cis/{id:D}", change, read.ETag);
        Assert.True(changed.Status == HttpStatusCode.OK, changed.ToString());
        Assert.NotEqual(read.ETag, changed.ETag);
        Assert.Equal("{\"costCentre\":\"CC-4410\",\"monitored\":true,\"backupTier\":\"Silver\"}", changed.Body.GetProperty("attributes").GetRawText());
        Assert.Equal([$"{id:D}|User|{editorPerson.Id:D}|{{\"type\":\"VirtualMachine\",\"name\":\"app-erp-db01\",\"fields\":[\"vCPUs reserved\",\"Patch window starts\",\"Backup tier\",\"Supports service\"]}}"],
            await scenario.AuditAsync("ci.changed"));
        var stale = await admin.PutAsync($"/api/v1/cis/{id:D}", new { name = "renamed" }, read.ETag);
        Assert.Equal((HttpStatusCode.PreconditionFailed, "changed"), (stale.Status, stale.Code));
        Assert.Equal("app-erp-db01", AuthScenario.Ok(await viewer.GetAsync($"/api/v1/cis/{id:D}")).Body.Value("name"));

        // Saving what is already there changes nothing: no new version, no audit entry.
        var same = AuthScenario.Ok(await editor.PutAsync($"/api/v1/cis/{id:D}", change, changed.ETag));
        Assert.Equal(changed.ETag, same.ETag);
        Assert.Single(await scenario.AuditAsync("ci.changed"));

        // Retired: hidden from search unless asked, changed only by restoring it.
        Assert.Equal(HttpStatusCode.PreconditionRequired, (await editor.PostAsync($"/api/v1/cis/{id:D}/retire")).Status);
        var retired = AuthScenario.Ok(await editor.SendAsync(HttpMethod.Post, $"/api/v1/cis/{id:D}/retire", new { }, ifMatch: same.ETag));
        Assert.Equal("Retired", retired.Body.Value("lifecycleState"));
        Assert.DoesNotContain(Names(await viewer.GetAsync("/api/v1/cis")), n => n == "app-erp-db01");
        Assert.Contains("app-erp-db01", Names(await viewer.GetAsync("/api/v1/cis?retired=true")));
        var whileRetired = await editor.PutAsync($"/api/v1/cis/{id:D}", change, retired.ETag);
        Assert.Equal((HttpStatusCode.Conflict, "retired"), (whileRetired.Status, whileRetired.Code));
        var again = await editor.SendAsync(HttpMethod.Post, $"/api/v1/cis/{id:D}/retire", new { }, ifMatch: retired.ETag);
        Assert.Equal((HttpStatusCode.Conflict, "retired"), (again.Status, again.Code));
        var restored = AuthScenario.Ok(await admin.SendAsync(HttpMethod.Post, $"/api/v1/cis/{id:D}/restore", new { }, ifMatch: retired.ETag));
        Assert.Equal("Active", restored.Body.Value("lifecycleState"));
        Assert.Equal(adminPerson.Id, restored.Body.GetProperty("changedBy").GetProperty("id").GetGuid());
        Assert.Single(await scenario.AuditAsync("ci.retired"));
        Assert.Equal([$"{id:D}|User|{adminPerson.Id:D}|{{\"type\":\"VirtualMachine\",\"name\":\"app-erp-db01\"}}"], await scenario.AuditAsync("ci.restored"));

        // The audit log names the CI and its type; the CI type for an added attribute.
        var (auditor, _) = await scenario.InviteAndAcceptAsync(admin, "ida.auditor", "Auditor");
        var entries = AuthScenario.Ok(await auditor.GetAsync("/api/v1/audit?action=ci.restored&action=type.attribute.added")).Body.GetProperty("entries");
        var restoredEntry = entries.EnumerateArray().First(e => e.Value("action") == "ci.restored");
        Assert.Equal(("Ci", "app-erp-db01", "VirtualMachine"), (restoredEntry.GetProperty("target").Value("type"),
            restoredEntry.GetProperty("target").Value("name"), restoredEntry.GetProperty("target").Value("ciType")));
        Assert.Equal("CI restored", restoredEntry.Value("label"));
        var attributeEntry = entries.EnumerateArray().First(e => e.Value("action") == "type.attribute.added");
        Assert.Equal(("CiType", "VirtualMachine"), (attributeEntry.GetProperty("target").Value("type"), attributeEntry.GetProperty("target").Value("name")));
    }

    [Fact]
    public async Task Names_and_values_are_checked_against_the_type_and_a_refused_CI_writes_nothing()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, _) = await scenario.SetUpAsync();
        var (editor, _) = await scenario.InviteAndAcceptAsync(admin, "eddie.editor", "Editor");
        await AddVmAttributesAsync(admin);
        var team = await CreateAsync(editor, new { type = "Team", name = "ERP Platform" });
        var oldService = await CreateAsync(editor, new { type = "BusinessService", name = "ERP Legacy" });
        AuthScenario.Ok(await editor.SendAsync(HttpMethod.Post, $"/api/v1/cis/{oldService.Value("id")}/retire", new { }, ifMatch: $"\"{oldService.Value("eTag")}\""));
        var before = await CountAsync(scenario);

        Assert.Equal("UnknownType", Field(await editor.PostAsync("/api/v1/cis", new { type = "Toaster", name = "x" }), "type"));
        Assert.Equal("Required", Field(await editor.PostAsync("/api/v1/cis", new { name = "x" }), "type"));
        var refused = await editor.PostAsync("/api/v1/cis", new
        {
            type = "VirtualMachine",
            name = "   ",
            attributes = new Dictionary<string, object?>
            {
                ["vcpusReserved"] = "eight",
                ["monitored"] = "yes",
                ["patchWindowStarts"] = "07/11/2026",
                ["backupTier"] = "Platinum",
                ["supportsService"] = team.Value("id"),
                ["colour"] = "red",
            },
        });
        Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
        var errors = refused.Body.GetProperty("errors");
        Assert.Equal(new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["name"] = "Required",
            ["attributes.costCentre"] = "Required",
            ["attributes.vcpusReserved"] = "InvalidNumber",
            ["attributes.monitored"] = "InvalidBool",
            ["attributes.patchWindowStarts"] = "InvalidDate",
            ["attributes.backupTier"] = "InvalidChoice",
            ["attributes.supportsService"] = "LinkedCiWrongType",
            ["attributes.colour"] = "UnknownAttribute",
        }, new SortedDictionary<string, string>(errors.EnumerateObject().ToDictionary(e => e.Name, e => e.Value[0].GetString()!), StringComparer.Ordinal));
        Assert.Equal("LinkedCiRetired", Field(await editor.PostAsync("/api/v1/cis", new
        {
            type = "VirtualMachine",
            name = "vm",
            attributes = new { costCentre = "CC-1", supportsService = oldService.Value("id") },
        }), "attributes.supportsService"));
        Assert.Equal("LinkedCiNotFound", Field(await editor.PostAsync("/api/v1/cis", new
        {
            type = "VirtualMachine",
            name = "vm",
            attributes = new { costCentre = "CC-1", supportsService = Guid.NewGuid().ToString("D") },
        }), "attributes.supportsService"));
        Assert.Equal("TooLong", Field(await editor.PostAsync("/api/v1/cis", new { type = "Team", name = new string('n', 401) }), "name"));
        Assert.Equal(before, await CountAsync(scenario));
        Assert.Equal(2, (await scenario.AuditAsync("ci.created")).Count); // the team and the old service only

        // A link left as stored isn't rechecked: the VM still saves after the service it supports is retired.
        var service = await CreateAsync(editor, new { type = "BusinessService", name = "ERP" });
        var vm = await CreateAsync(editor, new { type = "VirtualMachine", name = "app-erp-db01", attributes = new { costCentre = "CC-1", supportsService = service.Value("id") } });
        AuthScenario.Ok(await editor.SendAsync(HttpMethod.Post, $"/api/v1/cis/{service.Value("id")}/retire", new { }, ifMatch: $"\"{service.Value("eTag")}\""));
        var renamed = await editor.PutAsync($"/api/v1/cis/{vm.Value("id")}",
            new { name = "app-erp-db02", attributes = new { costCentre = "CC-1", supportsService = service.Value("id") } }, $"\"{vm.Value("eTag")}\"");
        Assert.True(renamed.Status == HttpStatusCode.OK, renamed.ToString());
        Assert.Equal("Retired", renamed.Body.GetProperty("references").GetProperty(service.Value("id")).Value("lifecycleState"));
    }

    [Fact]
    public async Task The_search_matches_part_of_a_name_in_any_case_with_its_filters_counts_orders_and_cursor()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, _) = await scenario.SetUpAsync();
        var (editor, _) = await scenario.InviteAndAcceptAsync(admin, "eddie.editor", "Editor");
        var (viewer, _) = await scenario.InviteAndAcceptAsync(admin, "vic.viewer", "Viewer");
        foreach (var (type, name) in new[]
        {
            ("VirtualMachine", "app-erp-db01"), ("Server", "APP-ERP-DB01"), ("VirtualMachine", "app-erp-web01"), ("Team", "ERP Platform"),
            ("SqlInstance", @"SQLPRD01\ERP"), ("VirtualMachine", "app-crm-web01"), ("Appliance", "100%_ready"), ("Appliance", "100x_ready"),
        })
        {
            await CreateAsync(editor, new { type, name });
        }

        var erp = AuthScenario.Ok(await viewer.GetAsync("/api/v1/cis?q=ERP")).Body;
        Assert.Equal(5, erp.GetProperty("total").GetInt64());
        // By name in any case; the VM and the server named alike come in either order (their IDs break the tie).
        Assert.Equal(["app-erp-db01", "app-erp-db01", "app-erp-web01", "erp platform", @"sqlprd01\erp"],
            Names(erp).Select(n => n.ToLowerInvariant()));
        Assert.Equal(new Dictionary<string, int> { ["Team"] = 1, ["VirtualMachine"] = 2, ["Server"] = 1, ["SqlInstance"] = 1 }, Counts(erp));

        // Ticking types narrows the list and the total, not the counts.
        var ticked = AuthScenario.Ok(await viewer.GetAsync("/api/v1/cis?q=erp&type=VirtualMachine&type=server")).Body;
        Assert.Equal(3, ticked.GetProperty("total").GetInt64());
        Assert.All(ticked.GetProperty("items").EnumerateArray(), i => Assert.Contains(i.Value("type"), TickedTypes));
        Assert.Equal(Counts(erp), Counts(ticked));

        // LIKE's wildcards are only themselves.
        Assert.Equal(["100%_ready"], Names(await viewer.GetAsync("/api/v1/cis?q=" + Uri.EscapeDataString("0%_"))));
        Assert.Equal([@"SQLPRD01\ERP"], Names(await viewer.GetAsync("/api/v1/cis?q=" + Uri.EscapeDataString(@"1\e"))));

        // Orders, and a cursor that walks the whole list with no overlap.
        var all = Names(await viewer.GetAsync("/api/v1/cis?limit=200"));
        Assert.Equal(8, all.Count);
        Assert.Equal(all.AsEnumerable().Reverse(), Names(await viewer.GetAsync("/api/v1/cis?sort=-name")));
        var byType = AuthScenario.Ok(await viewer.GetAsync("/api/v1/cis?sort=type")).Body.GetProperty("items").EnumerateArray().Select(i => i.Value("type")).ToList();
        Assert.Equal(byType.Order(StringComparer.OrdinalIgnoreCase), byType);
        var newest = Names(await viewer.GetAsync("/api/v1/cis?sort=-changed"));
        Assert.Equal("100x_ready", newest[0]);
        var walked = new List<string>();
        string? cursor = null;
        do
        {
            var page = AuthScenario.Ok(await viewer.GetAsync("/api/v1/cis?sort=-changed&limit=3" + (cursor is null ? "" : "&cursor=" + cursor))).Body;
            walked.AddRange(Names(page));
            Assert.Equal(cursor is null, page.GetProperty("total").ValueKind == JsonValueKind.Number);
            cursor = page.GetProperty("next").GetString();
        }
        while (cursor is not null);
        Assert.Equal(newest, walked);

        // Every parameter is checked.
        var firstPage = AuthScenario.Ok(await viewer.GetAsync("/api/v1/cis?limit=2")).Body.GetProperty("next").GetString();
        Assert.Equal("InvalidCursor", Field(await viewer.GetAsync($"/api/v1/cis?sort=-name&cursor={firstPage}"), "cursor"));
        Assert.Equal("InvalidCursor", Field(await viewer.GetAsync("/api/v1/cis?cursor=not-a-cursor"), "cursor"));
        // A cursor without the key its sort orders by would match no row and end the list: refused instead.
        var noName = Convert.ToBase64String(Encoding.UTF8.GetBytes($"[\"name\",null,null,null,\"{Guid.NewGuid():D}\"]")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal("InvalidCursor", Field(await viewer.GetAsync($"/api/v1/cis?sort=name&cursor={noName}"), "cursor"));
        Assert.Equal("UnknownType", Field(await viewer.GetAsync("/api/v1/cis?type=Toaster"), "type"));
        Assert.Equal("InvalidSort", Field(await viewer.GetAsync("/api/v1/cis?sort=owner"), "sort"));
        Assert.Equal("InvalidLimit", Field(await viewer.GetAsync("/api/v1/cis?limit=201"), "limit"));
        Assert.Equal("InvalidBoolean", Field(await viewer.GetAsync("/api/v1/cis?retired=yes"), "retired"));
        var missing = await viewer.GetAsync($"/api/v1/cis/{Guid.NewGuid():D}");
        Assert.Equal((HttpStatusCode.NotFound, "not-found"), (missing.Status, missing.Code));
    }

    [Fact]
    public async Task Two_editors_saving_the_same_version_one_wins_and_the_other_gets_412()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, _) = await scenario.SetUpAsync();
        var (editor, _) = await scenario.InviteAndAcceptAsync(admin, "eddie.editor", "Editor");
        var ci = await CreateAsync(editor, new { type = "Team", name = "ERP Platform" });
        var etag = $"\"{ci.Value("eTag")}\"";

        var results = await Task.WhenAll(
            editor.PutAsync($"/api/v1/cis/{ci.Value("id")}", new { name = "ERP Platform team", attributes = new { } }, etag),
            admin.PutAsync($"/api/v1/cis/{ci.Value("id")}", new { name = "ERP Platform group", attributes = new { } }, etag));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.PreconditionFailed], results.Select(r => r.Status).Order());
        Assert.Single(await scenario.AuditAsync("ci.changed"));
    }

    [Fact]
    public async Task A_number_keeps_every_digit_through_a_save_a_read_and_an_edit_of_another_field()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, _) = await scenario.SetUpAsync();
        var (editor, _) = await scenario.InviteAndAcceptAsync(admin, "eddie.editor", "Editor");
        var added = await admin.PostAsync("/api/v1/types/StorageArray/attributes", new { name = "capacityBytes", label = "Capacity (bytes)", dataType = "number" });
        Assert.True(added.Status == HttpStatusCode.Created, added.ToString());

        // Sent as a JSON number or a decimal string, a number comes back as a decimal string with every digit
        // and is stored as a JSON number (DEC-044 item 12).
        foreach (var (sent, expected) in new (object Sent, string Expected)[]
        {
            (1125899906842624m, "1125899906842624"), // 1 PiB in bytes
            (9007199254740993m, "9007199254740993"), // 2^53 + 1, which a JavaScript number rounds to ...992
            ("0.1", "0.1"),
            ("0.1234567890123456789", "0.1234567890123456789"),
            ("1.50", "1.50"),
        })
        {
            var array = await CreateAsync(editor, new { type = "StorageArray", name = $"array-{expected}", attributes = new { capacityBytes = sent } });
            Assert.Equal(expected, array.GetProperty("attributes").Value("capacityBytes"));
            Assert.Equal(expected, AuthScenario.Ok(await editor.GetAsync($"/api/v1/cis/{array.Value("id")}")).Body.GetProperty("attributes").Value("capacityBytes"));

            // Renaming sends the value back as read: it is unchanged, and the audit names only the name.
            var renamed = await editor.PutAsync($"/api/v1/cis/{array.Value("id")}",
                new { name = $"array-{expected}-renamed", attributes = new { capacityBytes = expected } }, $"\"{array.Value("eTag")}\"");
            Assert.True(renamed.Status == HttpStatusCode.OK, renamed.ToString());
            Assert.Equal(expected, renamed.Body.GetProperty("attributes").Value("capacityBytes"));
            Assert.Equal($"{{\"capacityBytes\":{expected}}}", await scenario.Sql.AdminScalarAsync<string>(scenario.Installation.Database,
                $"SELECT CAST(Attributes AS nvarchar(max)) FROM cmdb.Ci WHERE Id = '{array.Value("id")}';"));
        }

        Assert.All(await scenario.AuditAsync("ci.changed"), row => Assert.EndsWith(",\"fields\":[\"Name\"]}", row, StringComparison.Ordinal));
        Assert.Equal(5, (await scenario.AuditAsync("ci.changed")).Count);

        // Never rounded silently: beyond a decimal's precision or range, or not a number, it is refused.
        Assert.Equal("TooPrecise", NumberError(await editor.PostAsync("/api/v1/cis", Refused("0.12345678901234567890123456789012"))));
        Assert.Equal("TooPrecise", NumberError(await editor.PostAsync("/api/v1/cis", Refused("1e-40"))));
        Assert.Equal("NumberOutOfRange", NumberError(await editor.PostAsync("/api/v1/cis", Refused("79228162514264337593543950336"))));
        Assert.Equal("NumberOutOfRange", NumberError(await editor.PostAsync("/api/v1/cis", Refused("1e400"))));
        Assert.Equal("InvalidNumber", NumberError(await editor.PostAsync("/api/v1/cis", Refused("12 TB"))));

        static object Refused(string capacity) => new { type = "StorageArray", name = "refused", attributes = new { capacityBytes = capacity } };
        static string? NumberError(Response response) => Field(response, "attributes.capacityBytes");
    }

    [Fact]
    public async Task A_save_SQL_Server_picks_as_a_deadlock_victim_runs_once_more_and_PUT_needs_its_values()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, _) = await scenario.SetUpAsync();
        var (editor, _) = await scenario.InviteAndAcceptAsync(admin, "eddie.editor", "Editor");
        var added = await admin.PostAsync("/api/v1/types/BusinessService/attributes",
            new { name = "dependsOn", label = "Depends on", dataType = "ciRef", refType = "BusinessService" });
        Assert.True(added.Status == HttpStatusCode.Created, added.ToString());
        Assert.Equal("/api/v1/types", added.Headers["Location"]);
        var a = await CreateAsync(editor, new { type = "BusinessService", name = "ERP" });
        var b = await CreateAsync(editor, new { type = "BusinessService", name = "CRM" });
        var path = $"/api/v1/cis/{a.Value("id")}";
        var version = $"\"{a.Value("eTag")}\"";

        // PUT replaces every value, so it needs them (an empty object clears them); a CI can't link to itself.
        Assert.Equal("Required", Field(await editor.PutAsync(path, new { name = "ERP" }, version), "attributes"));
        Assert.Equal("LinkedCiSelf", Field(await editor.PutAsync(path, new { name = "ERP", attributes = new { dependsOn = a.Value("id") } }, version), "attributes.dependsOn"));

        // Another session holds a shared lock on ERP, as a save of CRM newly linking to ERP does, then needs CRM,
        // which the save of ERP linking to CRM holds: SQL Server makes that save its deadlock victim (the other
        // session has the higher priority), and the save runs again once the other session commits.
        await using var other = await scenario.Sql.OpenAsync(SqlServerFixture.AdminUser, scenario.Sql.AdminPassword, scenario.Installation.Database);
        await using var transaction = (SqlTransaction)await other.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var session = await ExecuteAsync<short>(other, transaction, "SET DEADLOCK_PRIORITY HIGH; SELECT @@SPID;");
        await ExecuteAsync<object>(other, transaction, $"SELECT Id FROM cmdb.Ci WITH (HOLDLOCK) WHERE Id = '{a.Value("id")}';");
        var save = editor.PutAsync(path, new { name = "ERP", attributes = new { dependsOn = b.Value("id") } }, version);
        for (var wait = 0; await scenario.Sql.AdminScalarAsync<int>(scenario.Installation.Database,
            $"SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id = {session};") == 0; wait++)
        {
            Assert.True(wait < 300, "The save never waited for the other session's lock.");
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        // The save holds CRM and waits for ERP, so this update closes the cycle: it returns once SQL Server has
        // rolled the save back, and the save's second run then waits for this commit.
        await ExecuteAsync<object>(other, transaction, $"UPDATE cmdb.Ci SET Name = Name WHERE Id = '{b.Value("id")}';");
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
        var saved = await save;

        Assert.True(saved.Status == HttpStatusCode.OK, saved.ToString());
        Assert.Equal(b.Value("id"), saved.Body.GetProperty("attributes").Value("dependsOn"));
        Assert.Single(await scenario.AuditAsync("ci.changed"));

        static async Task<T?> ExecuteAsync<T>(SqlConnection connection, SqlTransaction transaction, string text)
        {
            await using var command = new SqlCommand(text, connection, transaction);
            return (T?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        }
    }

    private static async Task AddVmAttributesAsync(ApiClient admin)
    {
        foreach (var attribute in new object[]
        {
            new { name = "costCentre", label = "Cost centre", dataType = "string", required = true },
            new { name = "vcpusReserved", label = "vCPUs reserved", dataType = "number" },
            new { name = "monitored", label = "Monitored", dataType = "bool" },
            new { name = "patchWindowStarts", label = "Patch window starts", dataType = "date" },
            new { name = "backupTier", label = "Backup tier", dataType = "enum", choices = new[] { "Gold", "Silver", "Bronze" } },
            new { name = "supportsService", label = "Supports service", dataType = "ciRef", refType = "BusinessService" },
        })
        {
            var response = await admin.PostAsync("/api/v1/types/VirtualMachine/attributes", attribute);
            Assert.True(response.Status == HttpStatusCode.Created, response.ToString());
        }
    }

    private static async Task<JsonElement> CreateAsync(ApiClient editor, object ci)
    {
        var response = await editor.PostAsync("/api/v1/cis", ci);
        Assert.True(response.Status == HttpStatusCode.Created, response.ToString());
        return response.Body;
    }

    private static Task<int> CountAsync(AuthScenario scenario) =>
        scenario.Sql.AdminScalarAsync<int>(scenario.Installation.Database, "SELECT COUNT(*) FROM cmdb.Ci;");

    private static JsonElement Type(IEnumerable<JsonElement> types, string name) => types.Single(t => t.GetProperty("name").GetString() == name);

    private static string? Field(Response response, string field)
    {
        Assert.True(response.Status == HttpStatusCode.BadRequest, response.ToString());
        return response.Body.GetProperty("errors").GetProperty(field)[0].GetString();
    }

    private static List<string> Names(Response response) => Names(AuthScenario.Ok(response).Body);

    private static List<string> Names(JsonElement page) => [.. page.GetProperty("items").EnumerateArray().Select(i => i.Value("name"))];

    private static Dictionary<string, int> Counts(JsonElement page) =>
        page.GetProperty("types").EnumerateArray().ToDictionary(t => t.Value("type"), t => t.GetProperty("count").GetInt32());
}

internal static class JsonElementTestExtensions
{
    public static string Value(this JsonElement element, string name) => element.GetProperty(name).GetString()!;
}

using System.Net;
using System.Text.Json;
using Goldmark.Api.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Goldmark.Api.IntegrationTests;

/// <summary>
/// Screen 14's API (US-10, FR-CM-1, FR-IM-1; DEC-045) against SQL Server 2025 with real sessions: custom CI types
/// added, hidden, shown and deleted only when unused; custom attributes changed and deleted, their values leaving
/// every CI in one change with every other digit kept; relationship types; the counts; each change audited in its
/// transaction, with the definitions' history kept (item 8); and the type's lock between a save and an Admin's
/// change. The 403s for a Viewer are in the M6 sweep (<see cref="AccountAndAccessTests"/>).
/// </summary>
[Collection(SqlServerTests.Name)]
public sealed class TypeApiTests(SqlServerFixture sql)
{
    private static readonly string[] Tiers = ["Gold", "Silver", "Bronze"];
    private static readonly string[] TiersWithoutGold = ["Silver", "Bronze", "Platinum"];
    private static readonly string[] TiersPlus = ["Gold", "Silver", "Bronze", "Platinum"];

    [Fact]
    public async Task An_Admin_adds_a_custom_type_hides_and_shows_it_and_deletes_only_an_unused_one()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, adminPerson) = await scenario.SetUpAsync();
        var (editor, _) = await scenario.InviteAndAcceptAsync(admin, "eddie.editor", "Editor");

        Assert.Equal(HttpStatusCode.Forbidden, (await editor.PostAsync("/api/v1/types", new { name = "Printer", category = "Network" })).Status);
        var added = await admin.PostAsync("/api/v1/types", new { name = "Printer", category = "Network", icon = "network-device" });
        Assert.True(added.Status == HttpStatusCode.Created, added.ToString());
        Assert.Equal(("Printer", "Network", "network-device", false, false), (added.Value("name"), added.Value("category"), added.Value("icon"),
            added.Body.GetProperty("builtIn").GetBoolean(), added.Body.GetProperty("hidden").GetBoolean()));
        var printerId = added.Body.GetProperty("id").GetInt32();
        Assert.Equal([$"{printerId}|User|{adminPerson.Id:D}|{{\"type\":\"Printer\",\"group\":\"Network\",\"icon\":\"network-device\"}}"],
            await scenario.AuditAsync("type.added"));

        // Names are unique in any case and never change; a group and an icon from the lists.
        Assert.Equal("DuplicateName", Field(await admin.PostAsync("/api/v1/types", new { name = "PRINTER", category = "Network" }), "name"));
        Assert.Equal("DuplicateName", Field(await admin.PostAsync("/api/v1/types", new { name = "Server", category = "Compute" }), "name"));
        Assert.Equal("InvalidName", Field(await admin.PostAsync("/api/v1/types", new { name = "printer", category = "Network" }), "name"));
        Assert.Equal("InvalidCategory", Field(await admin.PostAsync("/api/v1/types", new { name = "Fax", category = "Office" }), "category"));
        Assert.Equal("InvalidIcon", Field(await admin.PostAsync("/api/v1/types", new { name = "Fax", category = "Network", icon = "fax" }), "icon"));
        Assert.Single(await scenario.AuditAsync("type.added"));

        // A type with a CI can't be deleted, even when the CI is retired.
        var printer = await CreateAsync(editor, new { type = "Printer", name = "prn-floor-2" });
        AuthScenario.Ok(await editor.SendAsync(HttpMethod.Post, $"/api/v1/cis/{printer.Value("id")}/retire", new { }, ifMatch: Quoted(printer)));
        var refused = await admin.DeleteAsync("/api/v1/types/Printer");
        Assert.Equal((HttpStatusCode.Conflict, "has-cis"), (refused.Status, refused.Code));
        Assert.Equal("Printer has 1 CI (retired ones included), so it can't be deleted.", refused.Value("title"));

        // Hidden instead: no new CI of it, the existing one still works, links naming it still find it.
        var hidden = AuthScenario.Ok(await admin.PostAsync("/api/v1/types/printer/hide"));
        Assert.True(hidden.Body.GetProperty("hidden").GetBoolean());
        Assert.Equal([$"{printerId}|User|{adminPerson.Id:D}|{{\"type\":\"Printer\",\"cis\":1}}"], await scenario.AuditAsync("type.hidden"));
        Assert.True(Type(AuthScenario.Ok(await editor.GetAsync("/api/v1/types")).Body, "Printer").GetProperty("hidden").GetBoolean());
        Assert.Equal("HiddenType", Field(await editor.PostAsync("/api/v1/cis", new { type = "Printer", name = "prn-floor-3" }), "type"));
        var restored = AuthScenario.Ok(await editor.SendAsync(HttpMethod.Post, $"/api/v1/cis/{printer.Value("id")}/restore", new { },
            ifMatch: Quoted(AuthScenario.Ok(await editor.GetAsync($"/api/v1/cis/{printer.Value("id")}")).Body)));
        Assert.Equal("Active", restored.Body.Value("lifecycleState"));
        Assert.Equal(1L, AuthScenario.Ok(await editor.GetAsync("/api/v1/cis?type=Printer")).Body.GetProperty("total").GetInt64());
        var again = await admin.PostAsync("/api/v1/types/Printer/hide");
        Assert.Equal((HttpStatusCode.Conflict, "hidden"), (again.Status, again.Code));
        var builtIn = await admin.PostAsync("/api/v1/types/Server/hide");
        Assert.Equal((HttpStatusCode.Conflict, "built-in"), (builtIn.Status, builtIn.Code));

        Assert.False(AuthScenario.Ok(await admin.PostAsync("/api/v1/types/Printer/show")).Body.GetProperty("hidden").GetBoolean());
        Assert.Single(await scenario.AuditAsync("type.shown"));
        await CreateAsync(editor, new { type = "Printer", name = "prn-floor-3" });
        var notHidden = await admin.PostAsync("/api/v1/types/Printer/show");
        Assert.Equal((HttpStatusCode.Conflict, "not-hidden"), (notHidden.Status, notHidden.Code));

        // An unused one goes, with its attributes; the audit log still names it.
        var fax = Created(await admin.PostAsync("/api/v1/types", new { name = "Fax", category = "Network" }));
        Assert.Equal("generic", fax.Value("icon"));
        var faxId = fax.Body.GetProperty("id").GetInt32();
        Assert.True((await admin.PostAsync("/api/v1/types/Fax/attributes", new { name = "line", label = "Line", dataType = "string" })).Status == HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync("/api/v1/types/Fax")).Status);
        Assert.Null(CiTypeOrNull(AuthScenario.Ok(await admin.GetAsync("/api/v1/types")).Body, "Fax"));
        Assert.Equal([$"{faxId}|User|{adminPerson.Id:D}|{{\"type\":\"Fax\",\"group\":\"Network\",\"attributesDeleted\":1}}"],
            await scenario.AuditAsync("type.deleted"));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync("/api/v1/types/Fax")).Status);
        var builtInDelete = await admin.DeleteAsync("/api/v1/types/Server");
        Assert.Equal((HttpStatusCode.Conflict, "built-in"), (builtInDelete.Status, builtInDelete.Code));

        // ... and its definition's history keeps it (DEC-045 item 8).
        Assert.Equal(1, await scenario.Sql.AdminScalarAsync<int>(scenario.Installation.Database,
            $"SELECT COUNT(*) FROM cmdb.CiTypeHistory WHERE Id = {faxId};"));
        Assert.Equal("Line", await scenario.Sql.AdminScalarAsync<string>(scenario.Installation.Database,
            $"SELECT Label FROM cmdb.AttributeDefHistory WHERE CiTypeId = {faxId};"));

        var (auditor, _) = await scenario.InviteAndAcceptAsync(admin, "ida.auditor", "Auditor");
        var entries = AuthScenario.Ok(await auditor.GetAsync($"/api/v1/audit?target=type:{faxId}")).Body.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(["type.deleted", "type.attribute.added", "type.added"], entries.Select(e => e.Value("action")));
        Assert.All(entries, e => Assert.Equal(("CiType", "Fax", true, faxId),
            (e.GetProperty("target").Value("type"), e.GetProperty("target").Value("name"), e.GetProperty("target").GetProperty("removed").GetBoolean(),
                e.GetProperty("target").GetProperty("number").GetInt32())));
        Assert.Equal("InvalidTarget", AuditField(await auditor.GetAsync("/api/v1/audit?target=type:x")));
    }

    [Fact]
    public async Task A_type_another_definition_names_cant_be_deleted()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, _) = await scenario.SetUpAsync();
        Created(await admin.PostAsync("/api/v1/types", new { name = "Printer", category = "Network" }));
        Assert.True((await admin.PostAsync("/api/v1/types/Server/attributes",
            new { name = "supportsPrinter", label = "Supports printer", dataType = "ciRef", refType = "Printer" })).Status == HttpStatusCode.Created);

        var refused = await admin.DeleteAsync("/api/v1/types/Printer");
        Assert.Equal((HttpStatusCode.Conflict, "referenced"), (refused.Status, refused.Code));
        Assert.Equal("Supports printer (an attribute of Server) links to Printer, so it can't be deleted.", refused.Value("title"));

        Created(await admin.PostAsync("/api/v1/types", new { name = "Scanner", category = "Network" }));
        Assert.True((await admin.PostAsync("/api/v1/relationship-types", new { name = "scans_for", from = "Scanner", impact = "None" })).Status
            == HttpStatusCode.Created);
        var named = await admin.DeleteAsync("/api/v1/types/Scanner");
        Assert.Equal((HttpStatusCode.Conflict, "referenced"), (named.Status, named.Code));
        Assert.Equal("The relationship type scans_for names Scanner, so it can't be deleted.", named.Value("title"));
        Assert.Empty(await scenario.AuditAsync("type.deleted"));
    }

    [Fact]
    public async Task An_Admin_changes_a_custom_attribute_with_its_version_and_cant_remove_a_choice_in_use()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, adminPerson) = await scenario.SetUpAsync();
        var (editor, _) = await scenario.InviteAndAcceptAsync(admin, "eddie.editor", "Editor");
        var added = await admin.PostAsync("/api/v1/types/VirtualMachine/attributes", new { name = "backupTier", label = "Backup tier", dataType = "enum", choices = Tiers });
        Assert.True(added.Status == HttpStatusCode.Created, added.ToString());
        Assert.Equal(Quoted(added.Body), added.ETag);
        var path = "/api/v1/types/VirtualMachine/attributes/backupTier";

        // Retired CIs count: their value comes back with a restore.
        var vm = await CreateAsync(editor, new { type = "VirtualMachine", name = "app-01", attributes = new { backupTier = "Gold" } });
        AuthScenario.Ok(await editor.SendAsync(HttpMethod.Post, $"/api/v1/cis/{vm.Value("id")}/retire", new { }, ifMatch: Quoted(vm)));

        Assert.Equal(HttpStatusCode.PreconditionRequired, (await admin.PutAsync(path, new { label = "Tier", required = false, choices = Tiers })).Status);
        Assert.Equal("ChoiceInUse", Field(await admin.PutAsync(path, new { label = "Tier", required = false, choices = TiersWithoutGold }, added.ETag), "choices"));
        Assert.Equal("Required", Field(await admin.PutAsync(path, new { label = "Tier", choices = Tiers }, added.ETag), "required"));
        Assert.Equal("InvalidChoices", Field(await admin.PutAsync(path, new { label = "Tier", required = false }, added.ETag), "choices"));
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.PutAsync(path, new { label = "Tier", required = false, choices = Tiers }, added.ETag)).Status);
        Assert.Empty(await scenario.AuditAsync("type.attribute.changed"));

        var changed = await admin.PutAsync(path, new { label = "Tier", required = false, choices = TiersPlus }, added.ETag);
        Assert.True(changed.Status == HttpStatusCode.OK, changed.ToString());
        Assert.NotEqual(added.ETag, changed.ETag);
        Assert.Equal(("Tier", "Pat Admin"), (changed.Value("label"), changed.Body.GetProperty("changedBy").Value("displayName")));
        Assert.Equal(TiersPlus, AuthScenario.Strings(changed.Body.GetProperty("choices")));
        var audit = Assert.Single(await scenario.AuditAsync("type.attribute.changed"));
        Assert.EndsWith($"|User|{adminPerson.Id:D}|{{\"type\":\"VirtualMachine\",\"attribute\":\"Tier\",\"name\":\"backupTier\",\"changed\":[\"Label\",\"Choices\"]}}",
            audit, StringComparison.Ordinal);

        // The old version is refused, and kept in the definition's history.
        var stale = await admin.PutAsync(path, new { label = "Backup tier", required = true, choices = Tiers }, added.ETag);
        Assert.Equal((HttpStatusCode.PreconditionFailed, "changed"), (stale.Status, stale.Code));
        Assert.Equal("Backup tier", await scenario.Sql.AdminScalarAsync<string>(scenario.Installation.Database,
            "SELECT Label FROM cmdb.AttributeDefHistory WHERE Name = 'backupTier';"));

        // Saving what is there writes nothing; built-in attributes are locked.
        Assert.Equal(changed.ETag, AuthScenario.Ok(await admin.PutAsync(path, new { label = "Tier", required = false, choices = TiersPlus }, changed.ETag)).ETag);
        Assert.Single(await scenario.AuditAsync("type.attribute.changed"));
        var platform = Type(AuthScenario.Ok(await admin.GetAsync("/api/v1/types")).Body, "Server").GetProperty("attributes")[0];
        var locked = await admin.PutAsync("/api/v1/types/Server/attributes/platform", new { label = "OS platform", required = false, choices = Tiers },
            Quoted(platform));
        Assert.Equal((HttpStatusCode.Conflict, "built-in"), (locked.Status, locked.Code));

        // The counts screen 14 shows: CIs with a value per attribute and choice, retired ones included.
        var usage = AuthScenario.Ok(await admin.GetAsync("/api/v1/types/usage")).Body;
        var vmUsage = usage.GetProperty("types").EnumerateArray().Single(t => t.Value("name") == "VirtualMachine");
        Assert.Equal(1, vmUsage.GetProperty("cis").GetInt64());
        var tier = vmUsage.GetProperty("attributes").EnumerateArray().Single(a => a.Value("name") == "backupTier");
        Assert.Equal(1, tier.GetProperty("values").GetInt64());
        Assert.Equal("{\"Gold\":1,\"Silver\":0,\"Bronze\":0,\"Platinum\":0}", tier.GetProperty("choices").GetRawText());
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.GetAsync("/api/v1/types/usage")).Status);
    }

    [Fact]
    public async Task Deleting_an_attribute_takes_its_values_out_of_every_CI_in_one_change_and_keeps_every_other_digit()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, adminPerson) = await scenario.SetUpAsync();
        var (editor, editorPerson) = await scenario.InviteAndAcceptAsync(admin, "eddie.editor", "Editor");
        foreach (var attribute in new object[]
        {
            new { name = "costCentre", label = "Cost centre", dataType = "string" },
            new { name = "capacityBytes", label = "Capacity (bytes)", dataType = "number" },
            new { name = "big", label = "Big", dataType = "number" },
            new { name = "fraction", label = "Fraction", dataType = "number" },
        })
        {
            Assert.True((await admin.PostAsync("/api/v1/types/StorageArray/attributes", attribute)).Status == HttpStatusCode.Created);
        }

        // The owner's numbers (DEC-044 item 12) beside the value that goes; one array retired, one without the value.
        var exact = new { costCentre = "CC-4410", capacityBytes = "1125899906842624", big = "9007199254740993", fraction = "0.1234567890123456789" };
        var a = await CreateAsync(editor, new { type = "StorageArray", name = "array-a", attributes = exact });
        var b = await CreateAsync(editor, new { type = "StorageArray", name = "array-b", attributes = new { costCentre = "CC-5000" } });
        var c = await CreateAsync(editor, new { type = "StorageArray", name = "array-c", attributes = new { capacityBytes = "0.1" } });
        AuthScenario.Ok(await editor.SendAsync(HttpMethod.Post, $"/api/v1/cis/{b.Value("id")}/retire", new { }, ifMatch: Quoted(b)));
        var definition = Type(AuthScenario.Ok(await admin.GetAsync("/api/v1/types")).Body, "StorageArray").GetProperty("attributes")
            .EnumerateArray().Single(x => x.Value("name") == "costCentre");
        var path = "/api/v1/types/StorageArray/attributes/costCentre";

        Assert.Equal(HttpStatusCode.PreconditionRequired, (await admin.DeleteAsync(path)).Status);
        var deleted = await admin.SendAsync(HttpMethod.Delete, path, ifMatch: Quoted(definition));
        Assert.True(deleted.Status == HttpStatusCode.OK, deleted.ToString());
        Assert.Equal(2, deleted.Body.GetProperty("valuesRemoved").GetInt32());
        Assert.Equal([$"14|User|{adminPerson.Id:D}|{{\"type\":\"StorageArray\",\"attribute\":\"Cost centre\",\"name\":\"costCentre\",\"valuesRemoved\":2}}"],
            await scenario.AuditAsync("type.attribute.deleted")); // StorageArray is type 14

        // Every other value as it was, digit for digit, and each CI that had one is a new version by the Admin.
        var after = AuthScenario.Ok(await editor.GetAsync($"/api/v1/cis/{a.Value("id")}"));
        Assert.Equal("{\"capacityBytes\":\"1125899906842624\",\"big\":\"9007199254740993\",\"fraction\":\"0.1234567890123456789\"}",
            after.Body.GetProperty("attributes").GetRawText());
        Assert.Equal("{\"capacityBytes\":1125899906842624,\"big\":9007199254740993,\"fraction\":0.1234567890123456789}",
            await StoredAsync(scenario, a));
        Assert.Equal(adminPerson.Id, after.Body.GetProperty("changedBy").GetProperty("id").GetGuid());
        Assert.Equal("{}", await StoredAsync(scenario, b));
        Assert.Equal("{\"capacityBytes\":0.1}", await StoredAsync(scenario, c));
        Assert.Equal(editorPerson.Id, AuthScenario.Ok(await editor.GetAsync($"/api/v1/cis/{c.Value("id")}")).Body.GetProperty("changedBy").GetProperty("id").GetGuid());

        // The CIs' history keeps the values, the definition's history what they meant (DEC-045 item 8).
        Assert.Equal(2, await scenario.Sql.AdminScalarAsync<int>(scenario.Installation.Database,
            "SELECT COUNT(DISTINCT Id) FROM cmdb.CiHistory WHERE JSON_VALUE(Attributes, '$.costCentre') IS NOT NULL;"));
        Assert.Equal("Cost centre:String", await scenario.Sql.AdminScalarAsync<string>(scenario.Installation.Database,
            "SELECT CONCAT(Label, ':', DataType) FROM cmdb.AttributeDefHistory WHERE Name = 'costCentre';"));

        // An edit begun before the delete is "changed meanwhile"; the attribute is gone from the type.
        var stale = await editor.PutAsync($"/api/v1/cis/{a.Value("id")}", new { name = "array-a", attributes = exact }, Quoted(a));
        Assert.Equal((HttpStatusCode.PreconditionFailed, "changed"), (stale.Status, stale.Code));
        Assert.Equal("UnknownAttribute", Field(await editor.PutAsync($"/api/v1/cis/{a.Value("id")}", new { name = "array-a", attributes = exact }, after.ETag),
            "attributes.costCentre"));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(HttpMethod.Delete, path, ifMatch: Quoted(definition))).Status);
    }

    [Fact]
    public async Task An_Admin_adds_changes_and_deletes_a_custom_relationship_type()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, adminPerson) = await scenario.SetUpAsync();
        var (editor, _) = await scenario.InviteAndAcceptAsync(admin, "eddie.editor", "Editor");

        var builtIn = AuthScenario.Ok(await editor.GetAsync("/api/v1/types")).Body.GetProperty("relationshipTypes").EnumerateArray()
            .Single(r => r.Value("name") == "runs_on");
        Assert.Equal("A VM runs on a hypervisor host.", builtIn.Value("description"));

        var added = await admin.PostAsync("/api/v1/relationship-types",
            new { name = "replicates_to", from = "storagearray", to = "StorageArray", impact = "Down", description = " Array-based replication to a second array. " });
        Assert.True(added.Status == HttpStatusCode.Created, added.ToString());
        Assert.Equal(("StorageArray", "StorageArray", "Down", "Array-based replication to a second array.", false),
            (added.Value("from"), added.Value("to"), added.Value("impact"), added.Value("description"), added.Body.GetProperty("builtIn").GetBoolean()));
        var id = added.Body.GetProperty("id").GetInt32();
        Assert.Equal([$"{id}|User|{adminPerson.Id:D}|{{\"name\":\"replicates_to\",\"from\":\"StorageArray\",\"to\":\"StorageArray\",\"impact\":\"Down\"}}"],
            await scenario.AuditAsync("relationship-type.added"));

        Assert.Equal("DuplicateName", Field(await admin.PostAsync("/api/v1/relationship-types", new { name = "replicates_to", impact = "None" }), "name"));
        Assert.Equal("DuplicateName", Field(await admin.PostAsync("/api/v1/relationship-types", new { name = "runs_on", impact = "None" }), "name"));
        Assert.Equal("InvalidName", Field(await admin.PostAsync("/api/v1/relationship-types", new { name = "Replicates-To", impact = "None" }), "name"));
        Assert.Equal("UnknownType", Field(await admin.PostAsync("/api/v1/relationship-types", new { name = "x_of", from = "Nope", impact = "None" }), "from"));
        Assert.Equal("InvalidImpact", Field(await admin.PostAsync("/api/v1/relationship-types", new { name = "x_of", impact = "Up" }), "impact"));
        Assert.Equal("TooLong", Field(await admin.PostAsync("/api/v1/relationship-types", new { name = "x_of", impact = "None", description = new string('d', 401) }),
            "description"));
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.PostAsync("/api/v1/relationship-types", new { name = "x_of", impact = "None" })).Status);

        var path = "/api/v1/relationship-types/replicates_to";
        Assert.Equal(HttpStatusCode.PreconditionRequired, (await admin.PutAsync(path, new { impact = "None" })).Status);
        var changed = await admin.PutAsync(path, new { impact = "None", description = "Array-based replication." }, added.ETag);
        Assert.True(changed.Status == HttpStatusCode.OK, changed.ToString());
        Assert.Equal(("None", "Array-based replication."), (changed.Value("impact"), changed.Value("description")));
        Assert.Equal([$"{id}|User|{adminPerson.Id:D}|{{\"name\":\"replicates_to\",\"changed\":[\"Impact\",\"Description\"]}}"],
            await scenario.AuditAsync("relationship-type.changed"));
        var stale = await admin.PutAsync(path, new { impact = "Down" }, added.ETag);
        Assert.Equal((HttpStatusCode.PreconditionFailed, "changed"), (stale.Status, stale.Code));
        Assert.Equal("Down", await scenario.Sql.AdminScalarAsync<string>(scenario.Installation.Database,
            $"SELECT ImpactPropagation FROM cmdb.RelationshipTypeHistory WHERE Id = {id};"));
        var locked = await admin.PutAsync("/api/v1/relationship-types/runs_on", new { impact = "None" }, Quoted(builtIn));
        Assert.Equal((HttpStatusCode.Conflict, "built-in"), (locked.Status, locked.Code));

        Assert.Equal(HttpStatusCode.PreconditionFailed, (await admin.SendAsync(HttpMethod.Delete, path, ifMatch: added.ETag)).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(HttpMethod.Delete, path, ifMatch: changed.ETag)).Status);
        Assert.Single(await scenario.AuditAsync("relationship-type.deleted"));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(HttpMethod.Delete, path, ifMatch: changed.ETag)).Status);

        var (auditor, _) = await scenario.InviteAndAcceptAsync(admin, "ida.auditor", "Auditor");
        var entries = AuthScenario.Ok(await auditor.GetAsync($"/api/v1/audit?target=relationship-type:{id}")).Body.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(["relationship-type.deleted", "relationship-type.changed", "relationship-type.added"], entries.Select(e => e.Value("action")));
        Assert.All(entries, e => Assert.Equal(("RelationshipType", "replicates_to"), (e.GetProperty("target").Value("type"), e.GetProperty("target").Value("name"))));
    }

    [Fact]
    public async Task A_save_and_an_Admins_change_to_its_type_wait_for_each_other()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, _) = await scenario.SetUpAsync();
        var (editor, _) = await scenario.InviteAndAcceptAsync(admin, "eddie.editor", "Editor");
        Assert.True((await admin.PostAsync("/api/v1/types/Team/attributes", new { name = "costCentre", label = "Cost centre", dataType = "string" })).Status
            == HttpStatusCode.Created);
        var team = await CreateAsync(editor, new { type = "Team", name = "ERP Platform", attributes = new { costCentre = "CC-1" } });
        var definition = Type(AuthScenario.Ok(await admin.GetAsync("/api/v1/types")).Body, "Team").GetProperty("attributes")[0];

        // Another session holds Team's row (type 2) shared, as a save of a Team CI does until it commits: the delete
        // waits. By its ID, as the app locks it: a lookup by name would lock only the name's index entry.
        await using var other = await scenario.Sql.OpenAsync(SqlServerFixture.AdminUser, scenario.Sql.AdminPassword, scenario.Installation.Database);
        await using (var transaction = (SqlTransaction)await other.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            var session = await ExecuteAsync<short>(other, transaction, "SELECT @@SPID;");
            await ExecuteAsync<object>(other, transaction, "SELECT Id FROM cmdb.CiType WITH (HOLDLOCK) WHERE Id = 2;");
            var delete = admin.SendAsync(HttpMethod.Delete, "/api/v1/types/Team/attributes/costCentre", ifMatch: Quoted(definition));
            await WaitUntilBlockedAsync(scenario, session);
            Assert.False(delete.IsCompleted);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(1, AuthScenario.Ok(await delete).Body.GetProperty("valuesRemoved").GetInt32());
        }

        // And the other way: while an Admin's change holds the type, a save of one of its CIs waits (reads that join
        // the type's row wait too, for as long as the change takes).
        var current = AuthScenario.Ok(await editor.GetAsync($"/api/v1/cis/{team.Value("id")}"));
        await using (var transaction = (SqlTransaction)await other.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            var session = await ExecuteAsync<short>(other, transaction, "SELECT @@SPID;");
            await ExecuteAsync<object>(other, transaction, "SELECT Id FROM cmdb.CiType WITH (XLOCK, HOLDLOCK, ROWLOCK) WHERE Id = 2;");
            var save = editor.PutAsync($"/api/v1/cis/{team.Value("id")}", new { name = "ERP Platform team", attributes = new { } }, current.ETag);
            await WaitUntilBlockedAsync(scenario, session);
            Assert.False(save.IsCompleted);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
            Assert.Equal("ERP Platform team", AuthScenario.Ok(await save).Body.Value("name"));
        }

        static async Task<T?> ExecuteAsync<T>(SqlConnection connection, SqlTransaction transaction, string text)
        {
            await using var command = new SqlCommand(text, connection, transaction);
            return (T?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        }
    }

    private static async Task WaitUntilBlockedAsync(AuthScenario scenario, short session)
    {
        for (var wait = 0; await scenario.Sql.AdminScalarAsync<int>(scenario.Installation.Database,
            $"SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id = {session};") == 0; wait++)
        {
            Assert.True(wait < 300, "The request never waited for the other session's lock.");
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }

    private static Response Created(Response response)
    {
        Assert.True(response.Status == HttpStatusCode.Created, response.ToString());
        return response;
    }

    private static async Task<JsonElement> CreateAsync(ApiClient editor, object ci)
    {
        var response = await editor.PostAsync("/api/v1/cis", ci);
        Assert.True(response.Status == HttpStatusCode.Created, response.ToString());
        return response.Body;
    }

    private static Task<string> StoredAsync(AuthScenario scenario, JsonElement ci) =>
        scenario.Sql.AdminScalarAsync<string>(scenario.Installation.Database, $"SELECT CAST(Attributes AS nvarchar(max)) FROM cmdb.Ci WHERE Id = '{ci.Value("id")}';");

    private static string Quoted(JsonElement element, string property = "eTag") => $"\"{element.Value(property)}\"";

    private static JsonElement Type(JsonElement body, string name) => CiTypeOrNull(body, name) ?? throw new InvalidOperationException($"No type {name}");

    private static JsonElement? CiTypeOrNull(JsonElement body, string name) =>
        body.GetProperty("types").EnumerateArray().Where(t => t.Value("name") == name).Cast<JsonElement?>().FirstOrDefault();

    private static string? Field(Response response, string field)
    {
        Assert.True(response.Status == HttpStatusCode.BadRequest, response.ToString());
        return response.Body.GetProperty("errors").GetProperty(field)[0].GetString();
    }

    private static string? AuditField(Response response) => Field(response, "target");
}

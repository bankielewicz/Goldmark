using System.Net;
using System.Text.Json;
using Goldmark.Api.IntegrationTests.Infrastructure;

namespace Goldmark.Api.IntegrationTests;

/// <summary>
/// Slice 7's API (US-42, US-43, FR-IM-4; DEC-047) against SQL Server 2025 with real sessions: a CI's history with
/// who made each change and what differed, and the inventory's reads as of a past moment.
/// </summary>
[Collection(SqlServerTests.Name)]
public sealed class HistoryApiTests(SqlServerFixture sql)
{
    [Fact]
    public async Task A_CIs_history_lists_every_change_newest_first_with_who_made_it_and_what_differed()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, adminPerson) = await scenario.SetUpAsync();
        var (ann, annPerson) = await scenario.InviteAndAcceptAsync(admin, "ann.editor", "Editor");
        var (bob, bobPerson) = await scenario.InviteAndAcceptAsync(admin, "bob.editor", "Editor");
        var (viewer, _) = await scenario.InviteAndAcceptAsync(admin, "vic.viewer", "Viewer");
        await AddAttributeAsync(admin, new { name = "rack", label = "Rack", dataType = "string" });
        await AddAttributeAsync(admin, new { name = "cores", label = "Cores", dataType = "number" });

        var id = await CreateAsync(ann, new { type = "Server", name = "srv-01", attributes = new { rack = "R1", cores = "8" } });
        var host = await CreateAsync(ann, new { type = "HypervisorHost", name = "esx-01" });
        await ChangeAsync(bob, id, new { name = "srv-01b", attributes = new { rack = "R2", cores = "8" } });
        var added = await ann.PostAsync("/api/v1/relationships", new { type = "depends_on", from = id, to = host, redundancyGroup = "g1" });
        Assert.True(added.Status == HttpStatusCode.Created, added.ToString());
        var relationship = added.Value("id");
        var regrouped = await bob.PutAsync($"/api/v1/relationships/{relationship}", new { redundancyGroup = "g2" }, added.ETag);
        Assert.True(regrouped.Status == HttpStatusCode.OK, regrouped.ToString());
        Assert.Equal(HttpStatusCode.NoContent, (await ann.SendAsync(HttpMethod.Delete, $"/api/v1/relationships/{relationship}", ifMatch: regrouped.ETag)).Status);
        AuthScenario.Ok(await bob.SendAsync(HttpMethod.Post, $"/api/v1/cis/{id}/retire", new { }, ifMatch: (await bob.GetAsync($"/api/v1/cis/{id}")).ETag));
        AuthScenario.Ok(await ann.SendAsync(HttpMethod.Post, $"/api/v1/cis/{id}/restore", new { }, ifMatch: (await ann.GetAsync($"/api/v1/cis/{id}")).ETag));
        // The Admin renames the attribute, then deletes it: its value leaves the CI in the delete's transaction.
        AuthScenario.Ok(await admin.PutAsync("/api/v1/types/Server/attributes/rack", new { label = "Rack position", required = false },
            await AttributeETagAsync(admin, "rack")));
        AuthScenario.Ok(await admin.SendAsync(HttpMethod.Delete, "/api/v1/types/Server/attributes/rack", ifMatch: await AttributeETagAsync(admin, "rack")));

        // Everyone signed in reads it, newest first.
        var history = AuthScenario.Ok(await viewer.GetAsync($"/api/v1/cis/{id}/history")).Body;
        var items = history.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(8, history.GetProperty("total").GetInt32());
        Assert.Equal(JsonValueKind.Null, history.GetProperty("next").ValueKind);
        Assert.Equal(
        [
            $"changed by {adminPerson.Id:D}",
            $"restored by {annPerson.Id:D}",
            $"retired by {bobPerson.Id:D}",
            $"relationshipEnded by {annPerson.Id:D}", // the audit entry's actor, not Bob, who wrote it last
            $"relationshipChanged by {bobPerson.Id:D}",
            $"relationshipAdded by {annPerson.Id:D}",
            $"changed by {bobPerson.Id:D}",
            $"created by {annPerson.Id:D}",
        ], items.Select(i => $"{i.Value("kind")} by {i.GetProperty("by").Value("id")}"));
        Assert.Equal("Ann Editor", items[7].GetProperty("by").Value("displayName"));
        var times = items.Select(i => i.Value("at")).ToList();
        Assert.Equal(times.OrderByDescending(t => t, StringComparer.Ordinal), times);

        // What differed: the value that went with its attribute, under the label the attribute had last; a rename
        // and a value; everything the CI started with, the number with every digit as text.
        Assert.Equal(["attributes.rack|Rack position|string|removed|\"R2\"|"], Changes(items[0]));
        Assert.Empty(Changes(items[1]));
        Assert.Empty(Changes(items[2]));
        Assert.Equal(["redundancyGroup|Redundancy group||changed|\"g1\"|\"g2\""], Changes(items[4]));
        Assert.Equal(["name|Name|string|changed|\"srv-01\"|\"srv-01b\"", "attributes.rack|Rack|string|changed|\"R1\"|\"R2\""], Changes(items[6]));
        Assert.Equal(["name|Name|string|added||\"srv-01\"", "attributes.rack|Rack|string|added||\"R1\"", "attributes.cores|Cores|number|added||\"8\""],
            Changes(items[7]));

        // The relationship as the CI sees it, and the other CI as it is now.
        foreach (var entry in items.Skip(3).Take(3))
        {
            var about = entry.GetProperty("relationship");
            Assert.Equal((relationship, "depends_on", "dependencies", host),
                (about.Value("id"), about.Value("type"), about.Value("direction"), about.Value("ci")));
        }

        Assert.Equal("g1", items[5].GetProperty("relationship").Value("redundancyGroup"));
        Assert.Equal("g2", items[3].GetProperty("relationship").Value("redundancyGroup"));
        Assert.Equal("esx-01", history.GetProperty("references").GetProperty(host).Value("name"));

        // The host's history has the same relationship from its side, and nothing of the server's own changes.
        var hostItems = AuthScenario.Ok(await viewer.GetAsync($"/api/v1/cis/{host}/history")).Body.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["relationshipEnded", "relationshipChanged", "relationshipAdded", "created"], hostItems.Select(i => i.Value("kind")));
        Assert.All(hostItems.Take(3), i => Assert.Equal(("dependents", id), (i.GetProperty("relationship").Value("direction"), i.GetProperty("relationship").Value("ci"))));

        // Pages of three hold the same eight entries, each once.
        var paged = new List<string>();
        string? cursor = null;
        for (var page = 0; page < 5; page++)
        {
            var body = AuthScenario.Ok(await viewer.GetAsync($"/api/v1/cis/{id}/history?limit=3{(cursor is null ? "" : $"&cursor={cursor}")}")).Body;
            Assert.Equal(cursor is null ? JsonValueKind.Number : JsonValueKind.Null, body.GetProperty("total").ValueKind);
            paged.AddRange(body.GetProperty("items").EnumerateArray().Select(i => i.GetRawText()));
            cursor = body.GetProperty("next").GetString();
            if (cursor is null)
            {
                break;
            }
        }

        Assert.Equal(items.Select(i => i.GetRawText()), paged);

        // The history is the whole past: it takes no asOf, and the usual refusals.
        Assert.Equal("NotSupported", Field(await viewer.GetAsync($"/api/v1/cis/{id}/history?asOf=2026-01-01T00:00:00Z"), "asOf"));
        Assert.Equal("InvalidLimit", Field(await viewer.GetAsync($"/api/v1/cis/{id}/history?limit=0"), "limit"));
        Assert.Equal("InvalidCursor", Field(await viewer.GetAsync($"/api/v1/cis/{id}/history?cursor=nonsense"), "cursor"));
        Assert.Equal((HttpStatusCode.NotFound, "not-found"), Status(await viewer.GetAsync($"/api/v1/cis/{Guid.NewGuid():D}/history")));

        // Once the audit entry is gone (retention), nobody is named for the ending: never Bob, the last writer.
        await sql.AsAdminAsync(scenario.Installation.Database, "DELETE FROM cmdb.AuditLog WHERE Action = 'relationship.deleted';");
        var later = AuthScenario.Ok(await viewer.GetAsync($"/api/v1/cis/{id}/history")).Body.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal("relationshipEnded", later[3].Value("kind"));
        Assert.Equal(JsonValueKind.Null, later[3].GetProperty("by").ValueKind);
    }

    [Fact]
    public async Task With_asOf_the_reads_answer_as_they_would_have_at_that_moment()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, _) = await scenario.SetUpAsync();
        var (ann, annPerson) = await scenario.InviteAndAcceptAsync(admin, "ann.editor", "Editor");
        var (viewer, _) = await scenario.InviteAndAcceptAsync(admin, "vic.viewer", "Viewer");
        await AddAttributeAsync(admin, new { name = "rack", label = "Rack", dataType = "string" });
        var id = await CreateAsync(ann, new { type = "Server", name = "srv-01", attributes = new { rack = "R1" } });
        var host = await CreateAsync(ann, new { type = "HypervisorHost", name = "esx-01" });
        var added = await ann.PostAsync("/api/v1/relationships", new { type = "depends_on", from = id, to = host, redundancyGroup = "" });
        Assert.True(added.Status == HttpStatusCode.Created, added.ToString());

        // The moment the relationship was added, as the history gives it: everything above is in place.
        var then = AuthScenario.Ok(await viewer.GetAsync($"/api/v1/cis/{id}/history")).Body.GetProperty("items")[0].Value("at");
        var asOf = $"asOf={Uri.EscapeDataString(then)}";

        // Since then: a rename and a new value, the relationship ended, the host retired, a second server, a new label.
        await ChangeAsync(ann, id, new { name = "srv-01b", attributes = new { rack = "R2" } });
        Assert.Equal(HttpStatusCode.NoContent, (await ann.SendAsync(HttpMethod.Delete, $"/api/v1/relationships/{added.Value("id")}", ifMatch: added.ETag)).Status);
        AuthScenario.Ok(await ann.SendAsync(HttpMethod.Post, $"/api/v1/cis/{host}/retire", new { }, ifMatch: (await ann.GetAsync($"/api/v1/cis/{host}")).ETag));
        var second = await CreateAsync(ann, new { type = "Server", name = "srv-02" });
        AuthScenario.Ok(await admin.PutAsync("/api/v1/types/Server/attributes/rack", new { label = "Rack position", required = false },
            await AttributeETagAsync(admin, "rack")));

        // The CI as it was, read-only (no ETag header), and as it is.
        var past = AuthScenario.Ok(await viewer.GetAsync($"/api/v1/cis/{id}?{asOf}"));
        Assert.Equal(("srv-01", "R1", annPerson.Id.ToString("D")),
            (past.Body.Value("name"), past.Body.GetProperty("attributes").Value("rack"), past.Body.GetProperty("changedBy").Value("id")));
        Assert.Null(past.ETag);
        var now = AuthScenario.Ok(await viewer.GetAsync($"/api/v1/cis/{id}"));
        Assert.Equal(("srv-01b", "R2"), (now.Body.Value("name"), now.Body.GetProperty("attributes").Value("rack")));
        Assert.NotNull(now.ETag);

        // Its attribute's label then and now.
        Assert.Equal("Rack", RackLabel(AuthScenario.Ok(await viewer.GetAsync($"/api/v1/types?{asOf}")).Body));
        Assert.Equal("Rack position", RackLabel(AuthScenario.Ok(await viewer.GetAsync("/api/v1/types")).Body));

        // The search: the name of then, without the server added since; the host not yet retired.
        var searched = AuthScenario.Ok(await viewer.GetAsync($"/api/v1/cis?q=s&sort=name&{asOf}")).Body;
        Assert.Equal(["esx-01:Active", "srv-01:Active"], searched.GetProperty("items").EnumerateArray().Select(i => $"{i.Value("name")}:{i.Value("lifecycleState")}"));
        Assert.Equal(2, searched.GetProperty("total").GetInt64());
        Assert.Equal(["srv-01b", "srv-02"], AuthScenario.Ok(await viewer.GetAsync("/api/v1/cis?q=s&sort=name")).Body.GetProperty("items")
            .EnumerateArray().Select(i => i.Value("name")));
        // A cursor belongs to its moment.
        var first = AuthScenario.Ok(await viewer.GetAsync($"/api/v1/cis?q=s&sort=name&limit=1&{asOf}")).Body;
        var cursor = first.Value("next");
        Assert.Equal(["srv-01"], AuthScenario.Ok(await viewer.GetAsync($"/api/v1/cis?q=s&sort=name&limit=1&cursor={cursor}&{asOf}")).Body
            .GetProperty("items").EnumerateArray().Select(i => i.Value("name")));
        Assert.Equal("InvalidCursor", Field(await viewer.GetAsync($"/api/v1/cis?q=s&sort=name&limit=1&cursor={cursor}"), "cursor"));

        // Its relationships: the one ended since, to the host as it was.
        var related = AuthScenario.Ok(await viewer.GetAsync($"/api/v1/cis/{id}/relationships?{asOf}")).Body.GetProperty("dependencies");
        Assert.Equal(1, related.GetProperty("total").GetInt64());
        Assert.Equal(("esx-01", "Active"), (related.GetProperty("items")[0].GetProperty("to").Value("name"), related.GetProperty("items")[0].GetProperty("to").Value("lifecycleState")));
        Assert.Equal(0, AuthScenario.Ok(await viewer.GetAsync($"/api/v1/cis/{id}/relationships")).Body.GetProperty("dependencies").GetProperty("total").GetInt64());

        // The graph walks the relationships of then (FR-IM-4).
        var graph = AuthScenario.Ok(await viewer.GetAsync($"/api/v1/graph?ci={id}&{asOf}")).Body;
        Assert.Equal(["esx-01", "srv-01"], graph.GetProperty("nodes").EnumerateArray().Select(n => n.Value("name")).Order(StringComparer.Ordinal));
        Assert.Equal(1, graph.GetProperty("relationships").GetArrayLength());
        Assert.Equal(0, AuthScenario.Ok(await viewer.GetAsync($"/api/v1/graph?ci={id}")).Body.GetProperty("relationships").GetArrayLength());

        // Its export too, and the audit entry records the moment.
        var export = await viewer.PostAsync($"/api/v1/graph/export?ci={id}&{asOf}");
        Assert.True(export.Status == HttpStatusCode.OK, export.ToString());
        Assert.Equal("1", export.Headers["X-Goldmark-Entries"]);
        Assert.Contains($"\"asOf\":\"{then}\"", Assert.Single(await scenario.AuditAsync("relationship.exported")), StringComparison.Ordinal);

        // A CI that exists now and didn't then; a CI that never did.
        Assert.Equal((HttpStatusCode.NotFound, "not-found-then"), Status(await viewer.GetAsync($"/api/v1/cis/{second}?{asOf}")));
        Assert.Equal((HttpStatusCode.NotFound, "not-found-then"), Status(await viewer.GetAsync($"/api/v1/cis/{second}/relationships?{asOf}")));
        Assert.Equal((HttpStatusCode.NotFound, "not-found-then"), Status(await viewer.GetAsync($"/api/v1/graph?ci={second}&{asOf}")));
        Assert.Equal((HttpStatusCode.NotFound, "not-found"), Status(await viewer.GetAsync($"/api/v1/cis/{Guid.NewGuid():D}?{asOf}")));

        // Before anything was entered: no CIs, and the built-in types, which were always there.
        var early = AuthScenario.Ok(await viewer.GetAsync("/api/v1/cis?asOf=2000-01-01T00:00:00Z")).Body;
        Assert.Equal(0, early.GetProperty("total").GetInt64());
        Assert.Equal(20, AuthScenario.Ok(await viewer.GetAsync("/api/v1/types?asOf=2000-01-01T00:00:00Z")).Body.GetProperty("types").GetArrayLength());

        // Not a past UTC moment; and the reads that have no past refuse asOf rather than answer with today's data.
        Assert.Equal("FutureTime", Field(await viewer.GetAsync($"/api/v1/cis/{id}?asOf=2999-01-01T00:00:00Z"), "asOf"));
        Assert.Equal("InvalidTime", Field(await viewer.GetAsync("/api/v1/cis?asOf=yesterday"), "asOf"));
        Assert.Equal("InvalidTime", Field(await viewer.GetAsync($"/api/v1/graph?ci={id}&asOf=2026-11-01T10:00:00"), "asOf"));
        Assert.Equal("NotSupported", Field(await viewer.GetAsync($"/api/v1/relationships/{added.Value("id")}?{asOf}"), "asOf"));
        Assert.Equal("NotSupported", Field(await viewer.GetAsync($"/api/v1/cis/{id}/redundancy-groups?{asOf}"), "asOf"));
        Assert.Equal("NotSupported", Field(await admin.GetAsync($"/api/v1/types/usage?{asOf}"), "asOf"));
    }

    private static string RackLabel(JsonElement types) =>
        types.GetProperty("types").EnumerateArray().Single(t => t.Value("name") == "Server").GetProperty("attributes").EnumerateArray()
            .Single(a => a.Value("name") == "rack").Value("label");

    private static IEnumerable<string> Changes(JsonElement entry) => entry.GetProperty("changes").EnumerateArray().Select(c =>
        $"{c.Value("field")}|{c.Value("label")}|{c.GetProperty("kind").GetString()}|{c.Value("change")}|{Raw(c, "before")}|{Raw(c, "after")}");

    private static string Raw(JsonElement change, string name) =>
        change.GetProperty(name) is { ValueKind: not JsonValueKind.Null } value ? value.GetRawText() : "";

    private static async Task AddAttributeAsync(ApiClient admin, object attribute)
    {
        var response = await admin.PostAsync("/api/v1/types/Server/attributes", attribute);
        Assert.True(response.Status == HttpStatusCode.Created, response.ToString());
    }

    private static async Task<string> AttributeETagAsync(ApiClient admin, string name)
    {
        var attribute = AuthScenario.Ok(await admin.GetAsync("/api/v1/types")).Body.GetProperty("types").EnumerateArray()
            .Single(t => t.Value("name") == "Server").GetProperty("attributes").EnumerateArray().Single(a => a.Value("name") == name);
        return $"\"{attribute.Value("eTag")}\"";
    }

    private static async Task<string> CreateAsync(ApiClient editor, object ci)
    {
        var response = await editor.PostAsync("/api/v1/cis", ci);
        Assert.True(response.Status == HttpStatusCode.Created, response.ToString());
        return response.Value("id");
    }

    private static async Task ChangeAsync(ApiClient editor, string id, object ci)
    {
        var read = AuthScenario.Ok(await editor.GetAsync($"/api/v1/cis/{id}"));
        var response = await editor.PutAsync($"/api/v1/cis/{id}", ci, read.ETag);
        Assert.True(response.Status == HttpStatusCode.OK, response.ToString());
    }

    private static (HttpStatusCode Status, string? Code) Status(Response response) => (response.Status, response.Code);

    private static string? Field(Response response, string field)
    {
        Assert.True(response.Status == HttpStatusCode.BadRequest, response.ToString());
        return response.Body.GetProperty("errors").GetProperty(field)[0].GetString();
    }
}

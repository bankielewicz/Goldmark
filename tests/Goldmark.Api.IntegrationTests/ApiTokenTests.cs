using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Goldmark.Api.IntegrationTests.Infrastructure;

namespace Goldmark.Api.IntegrationTests;

/// <summary>
/// Slice 8's API tokens (US-06, US-07, PRD §5.7; DEC-048) against SQL Server 2025: an Admin creates a token and sees
/// its secret once; the token reads and changes the inventory within its scopes, without the anti-forgery token, as
/// itself, so a CI's <c>changedBy</c>, its history's <c>by</c> and the audit log name the token; it is refused
/// elsewhere (403), and when revoked, expired, unknown or malformed (401); its use is recorded at most once a minute
/// and audited at most once an hour; its requests are rate limited per token; and with a session cookie too, the
/// token alone acts. The M6 sweep with a token is in <see cref="AccountAndAccessTests"/>.
/// </summary>
[Collection(SqlServerTests.Name)]
public sealed class ApiTokenTests(SqlServerFixture sql)
{
    private static readonly string[] CisRead = ["cis:read"];
    private static readonly string[] CisReadWrite = ["cis:read", "cis:write"];
    private static readonly string[] Relationships = ["relationships:read", "relationships:write"];
    private static readonly string[] WriteReadRead = ["cis:write", "cis:read", "cis:read"];
    private static readonly string[] RelationshipsReadCisRead = ["relationships:read", "cis:read"];
    private static readonly string[] IngestWrite = ["ingest:write"];
    private static readonly string[] NotYet = ["lastUsedAt", "revokedAt", "revokedBy"];

    [Fact]
    public async Task An_Admin_creates_a_token_and_sees_its_secret_once_and_its_name_scopes_and_expiry_are_checked()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, adminPerson) = await scenario.SetUpAsync();
        var (editor, _) = await scenario.InviteAndAcceptAsync(admin, "eddie.editor", "Editor");
        var db = scenario.Installation.Database;
        var today = DateOnly.ParseExact(await sql.AdminScalarAsync<string>(db, "SELECT CONVERT(char(10), SYSUTCDATETIME(), 23);"), "yyyy-MM-dd",
            CultureInfo.InvariantCulture);

        var created = await admin.PostAsync("/api/v1/tokens", new { name = "  owner sync ", scopes = WriteReadRead, expiresInDays = 90 });

        Assert.True(created.Status == HttpStatusCode.Created, created.ToString());
        var token = created.Body;
        var id = token.GetProperty("id").GetGuid();
        var secret = token.Value("secret");
        Assert.Matches("^sxt_[0-9A-Za-z]{43}$", secret);
        Assert.Equal(("owner sync", secret[^4..], "active"), (token.Value("name"), token.Value("lastFour"), token.Value("status")));
        Assert.Equal(CisReadWrite, AuthScenario.Strings(token.GetProperty("scopes")));
        var createdBy = token.GetProperty("createdBy");
        Assert.Equal((adminPerson.Id, "Pat Admin", "pat.admin"), (createdBy.GetProperty("id").GetGuid(), createdBy.Value("name"), createdBy.Value("userName")));
        Assert.Equal(token.GetProperty("createdAt").GetDateTime().AddDays(90), token.GetProperty("expiresAt").GetDateTime());
        Assert.All(NotYet, p => Assert.Equal(JsonValueKind.Null, token.GetProperty(p).ValueKind));

        // Only the secret's SHA-256 and its last four characters are kept; it is never sent again, audited or logged.
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))),
            await sql.AdminScalarAsync<string>(db, $"SELECT CONVERT(char(64), Hash, 2) FROM cmdb.ApiToken WHERE Id = '{id}';"));
        var custom = await admin.PostAsync("/api/v1/tokens",
            new { name = "cmdb-export nightly", scopes = RelationshipsReadCisRead, expiresOn = Date(today.AddDays(1)) });
        Assert.True(custom.Status == HttpStatusCode.Created, custom.ToString());
        Assert.Equal(today.AddDays(2).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), custom.Body.GetProperty("expiresAt").GetDateTime().ToUniversalTime());
        var list = AuthScenario.Ok(await admin.GetAsync("/api/v1/tokens"));
        Assert.Equal(["cmdb-export nightly", "owner sync"], list.Body.GetProperty("tokens").EnumerateArray().Select(t => t.Value("name")));
        Assert.All(list.Body.GetProperty("tokens").EnumerateArray(), t => Assert.False(t.TryGetProperty("secret", out _)));
        Assert.DoesNotContain(secret, list.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, await scenario.AuditTextAsync() ?? "", StringComparison.Ordinal);
        Assert.DoesNotContain(secret["sxt_".Length..], scenario.App.Logs.RenderAll(), StringComparison.Ordinal);
        var audited = (await scenario.AuditAsync("token.created"))[0].Split('|');
        Assert.Equal([id.ToString("D"), "User", adminPerson.Id.ToString("D")], audited[..3]);
        using (var details = JsonDocument.Parse(audited[3]))
        {
            Assert.Equal(("owner sync", secret[^4..]), (details.RootElement.Value("name"), details.RootElement.Value("lastFour")));
            Assert.Equal(CisReadWrite, AuthScenario.Strings(details.RootElement.GetProperty("scopes")));
            Assert.Equal(token.GetProperty("expiresAt").GetDateTime(), details.RootElement.GetProperty("expiresAt").GetDateTime());
        }

        // Every field's errors at once; nothing is written for a refused request.
        Assert.Equal(new Dictionary<string, string>
        {
            ["name"] = "Required",
            ["scopes"] = "Required",
            ["expires"] = "Required",
        }, Errors(await admin.PostAsync("/api/v1/tokens", new { name = "  ", scopes = Array.Empty<string>() })));
        Assert.Equal(new Dictionary<string, string>
        {
            ["scopes"] = "UnknownScope",
            ["expiresInDays"] = "InvalidDays",
        }, Errors(await admin.PostAsync("/api/v1/tokens", new { name = "loader", scopes = IngestWrite, expiresInDays = 7 })));
        Assert.Equal(new Dictionary<string, string> { ["expires"] = "Ambiguous" },
            Errors(await admin.PostAsync("/api/v1/tokens", new { name = "loader", scopes = CisRead, expiresInDays = 30, expiresOn = Date(today.AddDays(5)) })));
        // A name is taken by any token, ignoring case and surrounding spaces; a custom date is from tomorrow to 365 days ahead.
        Assert.Equal(new Dictionary<string, string>
        {
            ["name"] = "Taken",
            ["expiresOn"] = "OutOfRange",
        }, Errors(await admin.PostAsync("/api/v1/tokens", new { name = " OWNER SYNC", scopes = CisRead, expiresOn = Date(today) })));
        Assert.Equal(new Dictionary<string, string> { ["expiresOn"] = "OutOfRange" },
            Errors(await admin.PostAsync("/api/v1/tokens", new { name = "loader", scopes = CisRead, expiresOn = Date(today.AddDays(366)) })));
        Assert.Equal(new Dictionary<string, string> { ["name"] = "TooLong" },
            Errors(await admin.PostAsync("/api/v1/tokens", new { name = new string('n', 101), scopes = CisRead, expiresInDays = 30 })));
        Assert.Equal(2, await sql.AdminScalarAsync<int>(db, "SELECT COUNT(*) FROM cmdb.ApiToken;"));
        Assert.Equal(2, (await scenario.AuditAsync("token.created")).Count);
        var yearAhead = await admin.PostAsync("/api/v1/tokens", new { name = "loader", scopes = CisRead, expiresOn = Date(today.AddDays(365)) });
        Assert.True(yearAhead.Status == HttpStatusCode.Created, yearAhead.ToString());

        // Only an Admin, signed in, manages tokens.
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.GetAsync("/api/v1/tokens")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.PostAsync("/api/v1/tokens", new { name = "mine", scopes = CisRead, expiresInDays = 30 })).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.PostAsync($"/api/v1/tokens/{id}/revoke")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.GetAsync($"/api/v1/tokens/{id}/usage")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await TokenClient(scenario, secret).GetAsync("/api/v1/tokens")).Status);
    }

    [Fact]
    public async Task A_token_reads_and_changes_the_inventory_within_its_scopes_as_itself_and_is_named_as_who_changed_it()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, adminPerson) = await scenario.SetUpAsync();
        var db = scenario.Installation.Database;
        var vm = (await CreateAsync(admin, new { type = "VirtualMachine", name = "app-erp-db01" })).Value("id");
        var host = (await CreateAsync(admin, new { type = "HypervisorHost", name = "esx-01" })).Value("id");
        var (_, readerSecret) = await CreateTokenAsync(admin, "reporting read", CisRead);
        var (syncId, syncSecret) = await CreateTokenAsync(admin, "owner sync", CisReadWrite);
        var (loaderId, loaderSecret) = await CreateTokenAsync(admin, "relationship loader", Relationships);
        var reader = TokenClient(scenario, readerSecret);
        var sync = TokenClient(scenario, syncSecret);
        var loader = TokenClient(scenario, loaderSecret);

        // cis:read reads CIs, their types, search and history.
        Assert.Contains("app-erp-db01", AuthScenario.Ok(await reader.GetAsync("/api/v1/cis")).Body.GetProperty("items").EnumerateArray().Select(i => i.Value("name")));
        var read = AuthScenario.Ok(await reader.GetAsync($"/api/v1/cis/{vm}"));
        AuthScenario.Ok(await reader.GetAsync("/api/v1/types"));
        AuthScenario.Ok(await reader.GetAsync($"/api/v1/cis/{vm}/history"));

        // Without the endpoint's scope, and wherever an endpoint declares none, a token gets 403.
        foreach (var (method, path) in new[]
        {
            (HttpMethod.Put, $"/api/v1/cis/{vm}"), (HttpMethod.Get, $"/api/v1/cis/{vm}/relationships"), (HttpMethod.Get, "/api/v1/graph?ci=" + vm),
            (HttpMethod.Get, "/api/v1/users"), (HttpMethod.Get, "/api/v1/tokens"), (HttpMethod.Get, "/api/v1/audit"), (HttpMethod.Get, "/api/v1/types/usage"),
            (HttpMethod.Post, "/api/v1/types"), (HttpMethod.Get, "/api/v1/account/two-factor"), (HttpMethod.Post, "/api/v1/auth/sign-out"),
        })
        {
            var refused = await reader.SendAsync(method, path, method == HttpMethod.Get ? null : new { name = "x" }, antiforgery: false, ifMatch: read.ETag);
            Assert.True(refused.Status == HttpStatusCode.Forbidden, $"{method} {path}: {refused}");
            Assert.Equal("application/problem+json", refused.ContentType);
            Assert.Equal("Bearer error=\"insufficient_scope\"", refused.Headers["WWW-Authenticate"]);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await loader.GetAsync($"/api/v1/cis/{vm}")).Status);

        // cis:write changes a CI without an anti-forgery token, as itself: ChangedBy, the CI, its history and the audit log name the token.
        var changed = await sync.SendAsync(HttpMethod.Put, $"/api/v1/cis/{vm}", new { name = "app-erp-db02", attributes = new { } }, antiforgery: false,
            ifMatch: read.ETag);
        Assert.True(changed.Status == HttpStatusCode.OK, changed.ToString());
        AssertToken(changed.Body.GetProperty("changedBy"), syncId, "owner sync");
        Assert.Equal($"token:{syncId:D}", await sql.AdminScalarAsync<string>(db, $"SELECT ChangedBy FROM cmdb.Ci WHERE Id = '{vm}';"));
        AssertToken(AuthScenario.Ok(await admin.GetAsync($"/api/v1/cis/{vm}")).Body.GetProperty("changedBy"), syncId, "owner sync");
        var history = AuthScenario.Ok(await admin.GetAsync($"/api/v1/cis/{vm}/history")).Body.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["changed", "created"], history.Select(i => i.Value("kind")));
        AssertToken(history[0].GetProperty("by"), syncId, "owner sync");
        Assert.Equal((adminPerson.Id, "user", "Pat Admin"),
            (history[1].GetProperty("by").GetProperty("id").GetGuid(), history[1].GetProperty("by").Value("kind"), history[1].GetProperty("by").Value("displayName")));
        var past = AuthScenario.Ok(await admin.GetAsync($"/api/v1/cis/{vm}?asOf={Uri.EscapeDataString(history[0].Value("at"))}"));
        AssertToken(past.Body.GetProperty("changedBy"), syncId, "owner sync");
        Assert.Equal([$"{vm}|Token|{syncId:D}|{{\"type\":\"VirtualMachine\",\"name\":\"app-erp-db02\",\"fields\":[\"Name\"]}}"],
            await scenario.AuditAsync("ci.changed"));

        // It creates, retires and restores too.
        var server = await sync.SendAsync(HttpMethod.Post, "/api/v1/cis", new { type = "Server", name = "srv-sync-01" }, antiforgery: false);
        Assert.True(server.Status == HttpStatusCode.Created, server.ToString());
        AssertToken(server.Body.GetProperty("changedBy"), syncId, "owner sync");
        var retired = AuthScenario.Ok(await sync.SendAsync(HttpMethod.Post, $"/api/v1/cis/{server.Value("id")}/retire", new { }, antiforgery: false, ifMatch: server.ETag));
        AuthScenario.Ok(await sync.SendAsync(HttpMethod.Post, $"/api/v1/cis/{server.Value("id")}/restore", new { }, antiforgery: false, ifMatch: retired.ETag));
        foreach (var action in new[] { "ci.created", "ci.retired", "ci.restored" })
        {
            Assert.Contains($"|Token|{syncId:D}|", await LastAsync(scenario, action), StringComparison.Ordinal);
        }

        // relationships:write adds, changes and ends relationships, and relationships:read reads them and the graph.
        var added = await loader.SendAsync(HttpMethod.Post, "/api/v1/relationships", new { type = "runs_on", from = vm, to = host }, antiforgery: false);
        Assert.True(added.Status == HttpStatusCode.Created, added.ToString());
        AssertToken(added.Body.GetProperty("changedBy"), loaderId, "relationship loader");
        var relationship = added.Value("id");
        AssertToken(AuthScenario.Ok(await loader.GetAsync($"/api/v1/relationships/{relationship}")).Body.GetProperty("changedBy"), loaderId, "relationship loader");
        var tab = AuthScenario.Ok(await loader.GetAsync($"/api/v1/cis/{vm}/relationships")).Body;
        AssertToken(tab.GetProperty("dependencies").GetProperty("items")[0].GetProperty("changedBy"), loaderId, "relationship loader");
        AuthScenario.Ok(await loader.GetAsync($"/api/v1/cis/{vm}/redundancy-groups"));
        AuthScenario.Ok(await loader.GetAsync($"/api/v1/graph?ci={vm}"));
        var regrouped = AuthScenario.Ok(await loader.SendAsync(HttpMethod.Put, $"/api/v1/relationships/{relationship}", new { redundancyGroup = "g1" }, antiforgery: false,
            ifMatch: added.ETag));
        Assert.Equal(HttpStatusCode.NoContent,
            (await loader.SendAsync(HttpMethod.Delete, $"/api/v1/relationships/{relationship}", antiforgery: false, ifMatch: regrouped.ETag)).Status);
        var entries = AuthScenario.Ok(await admin.GetAsync($"/api/v1/cis/{vm}/history")).Body.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["relationshipEnded", "relationshipChanged", "relationshipAdded", "changed", "created"], entries.Select(i => i.Value("kind")));
        Assert.All(entries.Take(3), e => AssertToken(e.GetProperty("by"), loaderId, "relationship loader"));
        foreach (var action in new[] { "relationship.added", "relationship.changed", "relationship.deleted" })
        {
            Assert.Contains($"|Token|{loaderId:D}|", await LastAsync(scenario, action), StringComparison.Ordinal);
        }

        // The audit log names the token as actor, filters by it (screen 18's "Open in audit log") and exports it so.
        var byToken = AuthScenario.Ok(await admin.GetAsync($"/api/v1/audit?actor=token:{syncId:D}")).Body.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(["ci.restored", "ci.retired", "ci.created", "ci.changed", "token.used"], byToken.Select(e => e.Value("action")));
        Assert.All(byToken, e => Assert.Equal(("Token", syncId, "owner sync"),
            (e.GetProperty("actor").Value("type"), e.GetProperty("actor").GetProperty("id").GetGuid(), e.GetProperty("actor").Value("name"))));
        Assert.Equal(("ApiToken", "owner sync"), (byToken[^1].GetProperty("target").Value("type"), byToken[^1].GetProperty("target").Value("name")));
        var export = await admin.PostAsync($"/api/v1/audit/export?actor=token:{syncId:D}");
        Assert.True(export.Status == HttpStatusCode.OK, export.ToString());
        Assert.Contains(",API token,owner sync (API token),CI changed,ci.changed,app-erp-db02 (VirtualMachine),", export.Text, StringComparison.Ordinal);
        Assert.Contains($"\"actor\":\"token:{syncId:D}\"", await LastAsync(scenario, "audit.exported"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revoked_expired_unknown_and_malformed_tokens_are_refused_and_each_use_is_recorded_at_most_once_a_minute_and_audited_once_an_hour()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, adminPerson) = await scenario.SetUpAsync();
        var db = scenario.Installation.Database;
        var (id, secret) = await CreateTokenAsync(admin, "owner sync", CisRead);
        var client = TokenClient(scenario, secret);
        var lastUsed = $"SELECT CONVERT(varchar(30), LastUsedAt, 126) FROM cmdb.ApiToken WHERE Id = '{id}';";

        // Concurrent requests write the last-used time once, and "API token used" once.
        var first = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.GetAsync("/api/v1/cis")));
        Assert.All(first, r => Assert.Equal(HttpStatusCode.OK, r.Status));
        var used = Assert.Single(await scenario.AuditAsync("token.used"));
        Assert.StartsWith($"{id}|Token|{id}|", used, StringComparison.Ordinal);
        var firstUse = await sql.AdminScalarAsync<string>(db, lastUsed);
        AuthScenario.Ok(await client.GetAsync("/api/v1/types"));
        Assert.Equal(firstUse, await sql.AdminScalarAsync<string>(db, lastUsed));

        // A minute on, the time moves and nothing is audited; an hour on, its use is audited again.
        await sql.AsAdminAsync(db, $"UPDATE cmdb.ApiToken SET LastUsedAt = DATEADD(minute, -2, LastUsedAt), UseRecordedAt = DATEADD(minute, -30, UseRecordedAt) WHERE Id = '{id}';");
        AuthScenario.Ok(await client.GetAsync("/api/v1/cis"));
        Assert.True(string.CompareOrdinal(await sql.AdminScalarAsync<string>(db, lastUsed), firstUse) > 0);
        Assert.Single(await scenario.AuditAsync("token.used"));
        await sql.AsAdminAsync(db, $"UPDATE cmdb.ApiToken SET LastUsedAt = DATEADD(minute, -2, LastUsedAt), UseRecordedAt = DATEADD(minute, -61, UseRecordedAt) WHERE Id = '{id}';");
        AuthScenario.Ok(await client.GetAsync("/api/v1/cis"));
        Assert.Equal(2, (await scenario.AuditAsync("token.used")).Count);
        Assert.NotEqual(JsonValueKind.Null, Token(AuthScenario.Ok(await admin.GetAsync("/api/v1/tokens")), id).GetProperty("lastUsedAt").ValueKind);

        // Its usage: its latest entries as actor, newest first, with their labels and targets.
        var usage = AuthScenario.Ok(await admin.GetAsync($"/api/v1/tokens/{id}/usage")).Body;
        Assert.Equal(("owner sync", "active"), (usage.GetProperty("token").Value("name"), usage.GetProperty("token").Value("status")));
        var entries = usage.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(2, entries.Count);
        Assert.True(entries[0].GetProperty("id").GetInt64() > entries[1].GetProperty("id").GetInt64());
        Assert.All(entries, e => Assert.Equal(("token.used", "API token used", "Succeeded", "ApiToken", id, "owner sync"),
            (e.Value("action"), e.Value("label"), e.Value("outcome"), e.GetProperty("target").Value("type"), e.GetProperty("target").GetProperty("id").GetGuid(),
                e.GetProperty("target").Value("name"))));

        // Revoked: refused with the next request, with the 401 a session gets and RFC 6750's header.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/v1/tokens/{id}/revoke")).Status);
        var refused = await client.GetAsync("/api/v1/cis");
        AssertUnauthorized(refused);
        Assert.Equal("Bearer error=\"invalid_token\"", refused.Headers["WWW-Authenticate"]);
        var revoked = Token(AuthScenario.Ok(await admin.GetAsync("/api/v1/tokens")), id);
        Assert.Equal(("revoked", adminPerson.Id), (revoked.Value("status"), revoked.GetProperty("revokedBy").GetProperty("id").GetGuid()));
        Assert.NotEqual(JsonValueKind.Null, revoked.GetProperty("revokedAt").ValueKind);
        var again = await admin.PostAsync($"/api/v1/tokens/{id}/revoke");
        Assert.Equal((HttpStatusCode.Conflict, "not-active"), (again.Status, again.Code));
        var none = await admin.PostAsync($"/api/v1/tokens/{Guid.CreateVersion7()}/revoke");
        Assert.Equal((HttpStatusCode.NotFound, "not-found"), (none.Status, none.Code));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/tokens/{Guid.CreateVersion7()}/usage")).Status);
        var revocation = Assert.Single(await scenario.AuditAsync("token.revoked"));
        Assert.Equal($"{id}|User|{adminPerson.Id:D}|{{\"name\":\"owner sync\",\"lastFour\":\"{secret[^4..]}\"}}", revocation);

        // Expired, by the database's clock: refused, listed so, and not revoked again.
        var (expiredId, expiredSecret) = await CreateTokenAsync(admin, "reporting read", CisRead);
        AuthScenario.Ok(await TokenClient(scenario, expiredSecret).GetAsync("/api/v1/cis"));
        await sql.AsAdminAsync(db, $"UPDATE cmdb.ApiToken SET ExpiresAt = DATEADD(second, -1, SYSUTCDATETIME()) WHERE Id = '{expiredId}';");
        AssertUnauthorized(await TokenClient(scenario, expiredSecret).GetAsync("/api/v1/cis"));
        Assert.Equal("expired", Token(AuthScenario.Ok(await admin.GetAsync("/api/v1/tokens")), expiredId).Value("status"));
        Assert.Equal("not-active", (await admin.PostAsync($"/api/v1/tokens/{expiredId}/revoke")).Code);

        // Unknown and malformed tokens: 401, and nothing recorded for them.
        var audited = (await scenario.AuditAsync("token.used")).Count;
        AssertUnauthorized(await TokenClient(scenario, "sxt_" + new string('7', 43)).GetAsync("/api/v1/cis"));
        AssertUnauthorized(await TokenClient(scenario, secret[..^1]).GetAsync("/api/v1/cis"));
        AssertUnauthorized(await TokenClient(scenario, secret.ToUpperInvariant()).GetAsync("/api/v1/cis"));
        Assert.Equal(audited, (await scenario.AuditAsync("token.used")).Count);
    }

    [Fact]
    public async Task A_use_that_cannot_be_recorded_never_fails_the_request_and_leaves_nothing_half_written()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, _) = await scenario.SetUpAsync();
        var db = scenario.Installation.Database;
        var (id, secret) = await CreateTokenAsync(admin, "owner sync", CisRead);
        var lastUsed = $"SELECT CONCAT(CONVERT(varchar(30), LastUsedAt, 126), '|', CONVERT(varchar(30), UseRecordedAt, 126)) FROM cmdb.ApiToken WHERE Id = '{id}';";

        await sql.AsAdminAsync(db, "DENY INSERT ON OBJECT::cmdb.AuditLog TO goldmark_runtime;");
        AuthScenario.Ok(await TokenClient(scenario, secret).GetAsync("/api/v1/cis"));
        Assert.Contains(scenario.App.Logs.Events, e => GoldmarkApp.IsEvent(e, 5001));
        Assert.Equal("|", await sql.AdminScalarAsync<string>(db, lastUsed)); // the time and the entry are one transaction
        Assert.Empty(await scenario.AuditAsync("token.used"));

        await sql.AsAdminAsync(db, "REVOKE INSERT ON OBJECT::cmdb.AuditLog FROM goldmark_runtime;");
        AuthScenario.Ok(await TokenClient(scenario, secret).GetAsync("/api/v1/cis"));
        Assert.Single(await scenario.AuditAsync("token.used"));
        Assert.NotEqual("|", await sql.AdminScalarAsync<string>(db, lastUsed));
    }

    [Fact]
    public async Task Each_tokens_requests_are_rate_limited_per_minute_with_429_and_Retry_After()
    {
        await using var scenario = await AuthScenario.StartAsync(sql, new Dictionary<string, string?> { ["Goldmark:Api:TokenRequestsPerMinute"] = "3" });
        var (admin, _) = await scenario.SetUpAsync();
        var (_, first) = await CreateTokenAsync(admin, "first", CisRead);
        var (_, second) = await CreateTokenAsync(admin, "second", CisRead);
        var client = TokenClient(scenario, first);

        for (var i = 0; i < 3; i++)
        {
            AuthScenario.Ok(await client.GetAsync("/api/v1/cis"));
        }

        var limited = await client.GetAsync("/api/v1/cis");
        Assert.Equal((HttpStatusCode.TooManyRequests, "rate-limited"), (limited.Status, limited.Code));
        Assert.Equal("application/problem+json", limited.ContentType);
        Assert.InRange(int.Parse(limited.Headers["Retry-After"], CultureInfo.InvariantCulture), 1, 60);
        Assert.True(limited.Headers.ContainsKey("Content-Security-Policy"));
        Assert.Equal("no-store", limited.CacheControl);
        // A refused request counts too.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/v1/users")).Status);

        // Each token has its own budget; a session has none.
        var other = TokenClient(scenario, second);
        for (var i = 0; i < 3; i++)
        {
            AuthScenario.Ok(await other.GetAsync("/api/v1/cis"));
        }

        for (var i = 0; i < 5; i++)
        {
            AuthScenario.Ok(await admin.GetAsync("/api/v1/cis"));
        }
    }

    [Fact]
    public async Task A_request_with_a_session_cookie_and_a_token_is_the_tokens_alone()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, adminPerson) = await scenario.SetUpAsync();
        var vm = (await CreateAsync(admin, new { type = "VirtualMachine", name = "app-erp-db01" })).Value("id");
        var (syncId, syncSecret) = await CreateTokenAsync(admin, "owner sync", CisReadWrite);

        // The Admin's browser session and the token: the token acts, needing no anti-forgery token, and reaches no Admin endpoint.
        admin.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", syncSecret);
        var read = AuthScenario.Ok(await admin.GetAsync($"/api/v1/cis/{vm}"));
        var changed = await admin.SendAsync(HttpMethod.Put, $"/api/v1/cis/{vm}", new { name = "app-erp-db02", attributes = new { } }, antiforgery: false,
            ifMatch: read.ETag);
        Assert.True(changed.Status == HttpStatusCode.OK, changed.ToString());
        AssertToken(changed.Body.GetProperty("changedBy"), syncId, "owner sync");
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/users")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.SendAsync(HttpMethod.Post, "/api/v1/tokens", new { name = "x", scopes = CisRead, expiresInDays = 30 },
            antiforgery: false)).Status);

        // A token that fails is 401 whatever session comes with it; without one, the session is as before.
        admin.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "sxt_" + new string('0', 43));
        AssertUnauthorized(await admin.GetAsync($"/api/v1/cis/{vm}"));
        admin.Http.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(adminPerson.Id.ToString("D"), AuthScenario.Ok(await admin.GetAsync("/api/v1/users")).Body.GetProperty("users")[0].Value("id"));

        // Another Authorization scheme, as a proxy's Basic or Negotiate, presents no token: the session is as before, anti-forgery too.
        admin.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", "cHJveHk6c2VjcmV0");
        var current = AuthScenario.Ok(await admin.GetAsync($"/api/v1/cis/{vm}"));
        Assert.True(ApiClient.IsAntiforgeryRefusal(await admin.SendAsync(HttpMethod.Put, $"/api/v1/cis/{vm}", new { name = "app-erp-db03", attributes = new { } },
            antiforgery: false, ifMatch: current.ETag)));
        AuthScenario.Ok(await admin.PutAsync($"/api/v1/cis/{vm}", new { name = "app-erp-db03", attributes = new { } }, current.ETag));
        admin.Http.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(
        [
            $"{vm}|Token|{syncId:D}|{{\"type\":\"VirtualMachine\",\"name\":\"app-erp-db02\",\"fields\":[\"Name\"]}}",
            $"{vm}|User|{adminPerson.Id:D}|{{\"type\":\"VirtualMachine\",\"name\":\"app-erp-db03\",\"fields\":[\"Name\"]}}",
        ], await scenario.AuditAsync("ci.changed"));
    }

    /// <summary>A script's client: the token in every request's Authorization header, no cookies, no anti-forgery token.</summary>
    private static ApiClient TokenClient(AuthScenario scenario, string secret)
    {
        var http = scenario.App.CreateDefaultClient(new Uri("https://localhost"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        return new ApiClient(http, new CookieContainer());
    }

    private static async Task<(Guid Id, string Secret)> CreateTokenAsync(ApiClient admin, string name, string[] scopes)
    {
        var created = await admin.PostAsync("/api/v1/tokens", new { name, scopes, expiresInDays = 90 });
        Assert.True(created.Status == HttpStatusCode.Created, created.ToString());
        return (created.Body.GetProperty("id").GetGuid(), created.Value("secret"));
    }

    private static async Task<JsonElement> CreateAsync(ApiClient client, object ci)
    {
        var response = await client.PostAsync("/api/v1/cis", ci);
        Assert.True(response.Status == HttpStatusCode.Created, response.ToString());
        return response.Body;
    }

    private static void AssertToken(JsonElement who, Guid id, string name) =>
        Assert.Equal((id, "token", name, JsonValueKind.Null),
            (who.GetProperty("id").GetGuid(), who.Value("kind"), who.Value("name"), who.GetProperty("displayName").ValueKind));

    private static void AssertUnauthorized(Response response)
    {
        Assert.True(response.Status == HttpStatusCode.Unauthorized, response.ToString());
        Assert.Equal("application/problem+json", response.ContentType);
        Assert.True(response.Headers.ContainsKey("Content-Security-Policy"));
        Assert.Equal("no-store", response.CacheControl);
    }

    private static async Task<string> LastAsync(AuthScenario scenario, string action) => (await scenario.AuditAsync(action))[^1];

    private static JsonElement Token(Response list, Guid id) =>
        list.Body.GetProperty("tokens").EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == id);

    private static Dictionary<string, string> Errors(Response response)
    {
        Assert.True(response.Status == HttpStatusCode.BadRequest, response.ToString());
        Assert.Equal("validation", response.Code);
        return response.Body.GetProperty("errors").EnumerateObject().ToDictionary(e => e.Name, e => e.Value[0].GetString()!);
    }

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

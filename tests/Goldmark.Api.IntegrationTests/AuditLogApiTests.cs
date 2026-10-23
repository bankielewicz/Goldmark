using System.Net;
using System.Text.Json;
using Goldmark.Api.Audit;
using Goldmark.Api.IntegrationTests.Infrastructure;

namespace Goldmark.Api.IntegrationTests;

/// <summary>
/// Screen 16's API (US-07, DEC-041) against SQL Server 2025, with real sessions: an Auditor reads and
/// filters the log page by page, names resolve (a removed account shows the user name its entries
/// recorded), reading writes nothing, an export writes its own entry in the same transaction, and an
/// export over the limit is refused without one. The Viewer's 403 is in the M6 sweep
/// (<see cref="AccountAndAccessTests"/>).
/// </summary>
[Collection(SqlServerTests.Name)]
public sealed class AuditLogApiTests(SqlServerFixture sql)
{
    [Fact]
    public async Task An_auditor_reads_and_filters_the_log_page_by_page_and_reading_writes_nothing()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, adminPerson) = await scenario.SetUpAsync();
        var (auditor, auditorPerson) = await scenario.InviteAndAcceptAsync(admin, "ida.auditor", "Auditor");
        var (_, _, samId) = await AuthScenario.InviteAsync(admin, "sam.rivera", "Viewer");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/users/{samId:D}/invitation")).Status); // removes the account
        var anonymous = scenario.App.Client();
        await anonymous.PostAsync("/api/v1/auth/sign-in", new { userName = auditorPerson.UserName, password = "not the password 1234" });
        await anonymous.PostAsync("/api/v1/auth/sign-in", new { userName = "no.such.person", password = "not the password 1234" });
        var rows = await RowCountAsync(scenario);

        var all = AuthScenario.Ok(await auditor.GetAsync("/api/v1/audit")).Body;
        var entries = all.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(rows, all.GetProperty("total").GetInt64());
        Assert.Equal(rows, entries.Count);
        Assert.Equal(entries.Select(Id).OrderDescending(), entries.Select(Id)); // newest first
        Assert.Equal(JsonValueKind.Null, all.GetProperty("next").ValueKind);

        var setup = Single(entries, "setup.completed");
        Assert.Equal("User", setup.GetProperty("actor").GetProperty("type").GetString());
        Assert.Equal(adminPerson.UserName, setup.GetProperty("actor").GetProperty("userName").GetString());
        Assert.Equal("Setup completed", setup.GetProperty("label").GetString());
        Assert.EndsWith("Z", setup.GetProperty("at").GetString(), StringComparison.Ordinal);

        var failures = entries.Where(e => Action(e) == "auth.sign-in.failed").ToList();
        Assert.Equal(2, failures.Count);
        Assert.All(failures, f => Assert.Equal("Failed", f.GetProperty("outcome").GetString()));
        Assert.All(failures, f => Assert.Equal("Anonymous", f.GetProperty("actor").GetProperty("type").GetString()));
        Assert.Contains(failures, f => f.GetProperty("target").ValueKind == JsonValueKind.Null); // the unknown name is not recorded
        var known = Assert.Single(failures, f => f.GetProperty("target").ValueKind == JsonValueKind.Object);
        Assert.Equal(auditorPerson.UserName, known.GetProperty("target").GetProperty("userName").GetString());
        Assert.Equal("password", known.GetProperty("details").GetProperty("reason").GetString());

        // Revoking the unused invitation removed Sam's account; the entries keep the recorded user name.
        var invited = entries.Where(e => Action(e) == "user.invitation.created").Select(e => e.GetProperty("target"))
            .Single(t => t.GetProperty("userName").GetString() == "sam.rivera");
        Assert.True(invited.GetProperty("removed").GetBoolean());
        Assert.Equal(samId, invited.GetProperty("id").GetGuid());

        // Filters.
        Assert.All(Entries(await auditor.GetAsync("/api/v1/audit?outcome=Failed")), e => Assert.Equal("Failed", e.GetProperty("outcome").GetString()));
        var aboutAuditor = Entries(await auditor.GetAsync($"/api/v1/audit?action=user.invitation.created&target={auditorPerson.Id:D}"));
        var invitation = Assert.Single(aboutAuditor);
        Assert.Equal(adminPerson.Id, invitation.GetProperty("actor").GetProperty("id").GetGuid());
        Assert.Equal(["Auditor"], AuthScenario.Strings(invitation.GetProperty("details").GetProperty("roles")));
        Assert.All(Entries(await auditor.GetAsync($"/api/v1/audit?actor={adminPerson.Id:D}")),
            e => Assert.Equal(adminPerson.Id, e.GetProperty("actor").GetProperty("id").GetGuid()));
        Assert.All(Entries(await auditor.GetAsync("/api/v1/audit?actor=Anonymous")),
            e => Assert.Equal("Anonymous", e.GetProperty("actor").GetProperty("type").GetString()));
        Assert.Empty(Entries(await auditor.GetAsync("/api/v1/audit?action=setup.completed&outcome=Failed"))); // narrowed to nothing
        Assert.Empty(Entries(await auditor.GetAsync("/api/v1/audit?from=2000-01-01T00:00:00Z&to=2000-01-02T00:00:00Z")));
        Assert.Equal(rows, Entries(await auditor.GetAsync($"/api/v1/audit?from={DateTime.UtcNow.AddHours(-1):O}")).Count);

        // Pages: a cursor continues where the last page ended, with no overlap.
        var first = AuthScenario.Ok(await auditor.GetAsync("/api/v1/audit?limit=2")).Body;
        var next = first.GetProperty("next").GetInt64();
        var second = Entries(await auditor.GetAsync($"/api/v1/audit?limit=2&cursor={next}"));
        Assert.Equal(next, Id(first.GetProperty("entries")[1]));
        Assert.All(second, e => Assert.True(Id(e) < next));
        Assert.Equal(entries.Skip(2).Take(2).Select(Id), second.Select(Id));
        Assert.Equal(entries.Select(Id).Min(), Id(Entries(await auditor.GetAsync("/api/v1/audit?order=oldest&limit=1"))[0]));

        // Every value is validated; none reaches SQL unchecked.
        foreach (var (query, field) in new[]
        {
            ("ip=10.%25", "ip"), ("action=nope", "action"), ("outcome=Maybe", "outcome"), ("from=2026-13-40", "from"),
            ("from=2026-10-20T00:00:00Z&to=2026-10-19T00:00:00Z", "to"), ("actor=someone", "actor"), ("target=1", "target"),
            ("limit=0", "limit"), ("limit=501", "limit"), ("cursor=-5", "cursor"), ("order=sideways", "order"),
        })
        {
            var invalid = await auditor.GetAsync($"/api/v1/audit?{query}");
            Assert.True(invalid.Status == HttpStatusCode.BadRequest, $"{query}: {invalid}");
            Assert.True(invalid.Body.GetProperty("errors").TryGetProperty(field, out _), $"{query}: {invalid}");
        }

        // The catalog and the people in the log (not the whole directory: Sam's account is gone).
        var catalog = AuthScenario.Ok(await auditor.GetAsync("/api/v1/audit/catalog")).Body;
        Assert.Equal(365, catalog.GetProperty("retentionDays").GetInt32());
        Assert.Equal(AuditCatalog.Actions.Select(a => a.Code), catalog.GetProperty("actions").EnumerateArray().Select(a => a.GetProperty("code").GetString()));
        var people = AuthScenario.Ok(await auditor.GetAsync("/api/v1/audit/people")).Body.GetProperty("people").EnumerateArray()
            .Select(p => p.GetProperty("userName").GetString()).ToList();
        Assert.Equal(new[] { adminPerson.UserName, auditorPerson.UserName }.Order(StringComparer.Ordinal), people.Order(StringComparer.Ordinal));

        // Reading wrote nothing; an Auditor still can't open Users & roles.
        Assert.Equal(rows, await RowCountAsync(scenario));
        Assert.Equal(HttpStatusCode.Forbidden, (await auditor.GetAsync("/api/v1/users")).Status);
    }

    [Fact]
    public async Task An_export_is_recorded_with_its_filters_and_one_over_the_limit_is_refused_without_an_entry()
    {
        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, _) = await scenario.SetUpAsync();
        var (auditor, auditorPerson) = await scenario.InviteAndAcceptAsync(admin, "ida.auditor", "Auditor");
        await scenario.App.Client().PostAsync("/api/v1/auth/sign-in", new { userName = auditorPerson.UserName, password = "not the password 1234" });

        var csv = await auditor.PostAsync("/api/v1/audit/export?outcome=Failed");

        Assert.True(csv.Status == HttpStatusCode.OK, csv.ToString());
        Assert.StartsWith("text/csv", csv.ContentType, StringComparison.Ordinal);
        Assert.Equal("1", csv.Headers[AuditEndpoints.EntriesHeader]);
        var lines = csv.Text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Time (UTC),Actor type,Actor,Action,Action code,Affected,IP address,Outcome,Details", lines[0]);
        var row = Assert.Single(lines.Skip(1));
        Assert.Contains(",Not signed in,,Sign-in failed,auth.sign-in.failed,Ida Auditor (ida.auditor),", row, StringComparison.Ordinal);
        var exported = Assert.Single(await scenario.AuditAsync(AuditActions.Exported));
        Assert.StartsWith($"|User|{auditorPerson.Id:D}|", exported, StringComparison.Ordinal);
        using (var details = JsonDocument.Parse(exported.Split('|')[3]))
        {
            Assert.Equal(1, details.RootElement.GetProperty("entries").GetInt32());
            Assert.Equal("Failed", details.RootElement.GetProperty("filters").GetProperty("outcome").GetString());
        }

        // Over the limit: refused before reading, and nothing is recorded.
        await sql.AsAdminAsync(scenario.Installation.Database, $"""
            INSERT INTO cmdb.AuditLog (ActorType, Action)
            SELECT TOP ({AuditLog.ExportLimit}) 'System', 'it.bulk' FROM sys.all_columns AS a CROSS JOIN sys.all_columns AS b;
            """);
        var tooMany = await auditor.PostAsync("/api/v1/audit/export");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooMany.Status);
        Assert.Equal("export-too-large", tooMany.Code);
        Assert.True(tooMany.Body.GetProperty("entries").GetInt64() > AuditLog.ExportLimit);
        Assert.Single(await scenario.AuditAsync(AuditActions.Exported));
    }

    private static long Id(JsonElement entry) => entry.GetProperty("id").GetInt64();

    private static string? Action(JsonElement entry) => entry.GetProperty("action").GetString();

    private static JsonElement Single(IEnumerable<JsonElement> entries, string action) => Assert.Single(entries, e => Action(e) == action);

    private static List<JsonElement> Entries(Response response) =>
        [.. AuthScenario.Ok(response).Body.GetProperty("entries").EnumerateArray()];

    private static Task<long> RowCountAsync(AuthScenario scenario) =>
        scenario.Sql.AdminScalarAsync<long>(scenario.Installation.Database, "SELECT COUNT_BIG(*) FROM cmdb.AuditLog;");
}

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Goldmark.Api.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Goldmark.Api.IntegrationTests;

/// <summary>
/// NFR-PERF-2 on 25,000 synthetic CIs (slice 5's "Done when"; DEC-044 item 7): the p95 of the CI list
/// and search at most 500 ms, of a CI's detail at most 300 ms, measured through the API against SQL
/// Server 2025 Express. It runs on request only (GOLDMARK_PERF=1; the Performance workflow's "Run
/// workflow"), not on every PR, and writes its figures to the job summary. The start of NFR-PERF-5's
/// suite: later slices add relationships, impact and ingest.
/// </summary>
[Collection(SqlServerTests.Name)]
public sealed class PerformanceTests(SqlServerFixture sql)
{
    private const int Warmup = 10;
    private const int Samples = 100;

    [Fact]
    public async Task The_CI_list_search_and_detail_meet_their_p95_on_25000_CIs()
    {
        if (Environment.GetEnvironmentVariable("GOLDMARK_PERF") != "1")
        {
            Assert.Skip("On request only: set GOLDMARK_PERF=1 (the Performance workflow does).");
        }

        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, adminPerson) = await scenario.SetUpAsync();
        await SyntheticInventory.SeedAsync(scenario.Sql, scenario.Installation, adminPerson.Id);
        Assert.Equal(SyntheticInventory.Count, await scenario.Sql.AdminScalarAsync<int>(scenario.Installation.Database, "SELECT COUNT(*) FROM cmdb.Ci;"));
        var ids = (await admin.GetAsync("/api/v1/cis?limit=200&sort=-changed")).Body.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetString()!).ToList();

        var results = new List<(string What, string Path, double P95, double Budget)>
        {
            await MeasureAsync(admin, "List, first page by name", _ => "/api/v1/cis", 500),
            await MeasureAsync(admin, "Search by name (\"erp\")", _ => "/api/v1/cis?q=erp", 500),
            await MeasureAsync(admin, "Search, two types ticked", _ => "/api/v1/cis?q=app-1&type=VirtualMachine&type=Server", 500),
            await MeasureAsync(admin, "Newest changes first, with retired", _ => "/api/v1/cis?sort=-changed&retired=true", 500),
            await MeasureAsync(admin, "A CI's detail", i => $"/api/v1/cis/{ids[i % ids.Count]}", 300),
        };

        var summary = new StringBuilder($"### CI list, search and detail on {SyntheticInventory.Count:N0} CIs (NFR-PERF-2)\n\n")
            .AppendLine(CultureInfo.InvariantCulture, $"{Samples} requests each after {Warmup} to warm up, through the API, SQL Server 2025 Express.\n")
            .AppendLine("| Request | p95 | Budget |")
            .AppendLine("|---|---:|---:|");
        foreach (var (what, path, p95, budget) in results)
        {
            summary.AppendLine(CultureInfo.InvariantCulture, $"| {what} (`{path}`) | {p95:F0} ms | {budget:F0} ms |");
        }

        TestContext.Current.TestOutputHelper?.WriteLine(summary.ToString());
        if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } file)
        {
            await File.AppendAllTextAsync(file, summary.ToString(), TestContext.Current.CancellationToken);
        }

        Assert.All(results, r => Assert.True(r.P95 <= r.Budget, $"{r.What}: p95 {r.P95:F0} ms, budget {r.Budget:F0} ms"));
    }

    /// <summary>
    /// What waits while an Admin deletes an attribute that many CIs hold (DEC-045 items 2 and 6): the delete takes
    /// the type's row exclusively, then rewrites every CI holding a value in one statement. Measured, not budgeted:
    /// at two sizes, the delete request and the longest read and save of a CI of another type (Servers) issued in a
    /// loop while it runs, with SQL Server's lock-escalation count on <c>cmdb.Ci</c> (a statement past about 5,000
    /// locks may take the whole table). The escalation is asserted, as HANDOFF §3 relies on it; the times are only
    /// reported. Runs with the p95 test, on request only.
    /// </summary>
    [Fact]
    public async Task Deleting_an_attribute_many_CIs_hold_and_what_waits_for_it()
    {
        if (Environment.GetEnvironmentVariable("GOLDMARK_PERF") != "1")
        {
            Assert.Skip("On request only: set GOLDMARK_PERF=1 (the Performance workflow does).");
        }

        await using var scenario = await AuthScenario.StartAsync(sql);
        var (admin, adminPerson) = await scenario.SetUpAsync();
        await SyntheticInventory.SeedAsync(scenario.Sql, scenario.Installation, adminPerson.Id);
        var database = scenario.Installation.Database;
        var servers = (await admin.GetAsync("/api/v1/cis?type=Server&limit=200")).Body.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetString()!).ToList();
        await using var watcher = await scenario.Sql.OpenAsync(SqlServerFixture.AdminUser, scenario.Sql.AdminPassword, database);

        var rows = new List<string>();
        var next = 0;
        foreach (var size in new[] { 4_000, 10_000 })
        {
            var name = $"costCentre{size}";
            var added = await admin.PostAsync("/api/v1/types/VirtualMachine/attributes", new { name, label = $"Cost centre {size}", dataType = "string" });
            Assert.True(added.Status == HttpStatusCode.Created, added.ToString());
            await scenario.Sql.AsAdminAsync(database, $"""
                UPDATE TOP ({size}) cmdb.Ci SET Attributes = JSON_MODIFY(Attributes, '$.{name}', N'CC-4410')
                WHERE CiTypeId = (SELECT Id FROM cmdb.CiType WHERE Name = N'VirtualMachine');
                """);

            // Without the delete: the longest of a few reads and saves of Servers, to compare against.
            var readsAlone = new List<double>();
            var savesAlone = new List<double>();
            for (var i = 0; i < 5; i++)
            {
                readsAlone.Add(await TimeAsync(() => admin.GetAsync($"/api/v1/cis/{servers[next]}")));
                var server = await admin.GetAsync($"/api/v1/cis/{servers[next++]}");
                savesAlone.Add(await TimeAsync(() => SaveAsync(admin, server)));
            }

            // The Servers the save loop renames, read before the delete: a read while it runs would wait itself, and
            // the save behind it would be timed only after the delete had ended.
            var toSave = new List<Response>();
            for (var i = 0; i < 60; i++)
            {
                toSave.Add(await admin.GetAsync($"/api/v1/cis/{servers[next++]}"));
            }

            // One loop of reads and one of saves; the delete goes once each has sent its first request, so neither can
            // miss it, and both run until it has answered. A request sent before the delete takes its lock just
            // doesn't wait.
            var readsDuring = new List<double>();
            var savesDuring = new List<double>();
            var readsStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var savesStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var stop = new CancellationTokenSource();
            var reads = Task.Run(async () =>
            {
                for (var i = 0; !stop.IsCancellationRequested; i++)
                {
                    var read = TimeAsync(() => admin.GetAsync($"/api/v1/cis/{servers[i % 20]}"));
                    readsStarted.TrySetResult();
                    readsDuring.Add(await read);
                }
            }, TestContext.Current.CancellationToken);
            var saves = Task.Run(async () =>
            {
                for (var i = 0; !stop.IsCancellationRequested && i < toSave.Count; i++)
                {
                    var save = TimeAsync(() => SaveAsync(admin, toSave[i]));
                    savesStarted.TrySetResult();
                    savesDuring.Add(await save);
                }
            }, TestContext.Current.CancellationToken);
            await Task.WhenAll(readsStarted.Task, savesStarted.Task);

            var escalationsBefore = await EscalationsAsync(watcher);
            var watch = Stopwatch.StartNew();
            var deleted = await admin.SendAsync(HttpMethod.Delete, $"/api/v1/types/VirtualMachine/attributes/{name}", ifMatch: added.ETag);
            var deleteMs = watch.Elapsed.TotalMilliseconds;
            await stop.CancelAsync();
            await Task.WhenAll(reads, saves);
            Assert.True(deleted.Status == HttpStatusCode.OK, deleted.ToString());
            Assert.Equal(size, deleted.Body.GetProperty("valuesRemoved").GetInt32());

            // Observed on SQL Server 2025 at both sizes, and what HANDOFF §3 relies on: a change to SQL Server's escalation
            // or a LOCK_ESCALATION setting on cmdb.Ci fails this, and the docs change with it. (The counter resets if the
            // table's metadata leaves the cache, which a busy test instance doesn't do between these two reads.)
            var escalations = await EscalationsAsync(watcher) - escalationsBefore;
            Assert.True(escalations >= 1, $"No lock escalation on cmdb.Ci while {size:N0} values were removed.");

            rows.Add(string.Create(CultureInfo.InvariantCulture,
                $"| {size:N0} | {deleteMs:F0} ms | {Longest(readsDuring)} of {readsDuring.Count} ({readsAlone.Max():F0}) | {Longest(savesDuring)} of {savesDuring.Count} ({savesAlone.Max():F0}) | {escalations} |"));
        }

        var sqlServer = await scenario.Sql.AdminScalarAsync<string>(database,
            "SELECT CONCAT('SQL Server ', CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(32)), ' ', CAST(SERVERPROPERTY('Edition') AS nvarchar(64)));");
        var summary = new StringBuilder("### Deleting an attribute many VMs hold (DEC-045 items 2 and 6)\n\n")
            .AppendLine(CultureInfo.InvariantCulture, $"{SyntheticInventory.Count:N0} synthetic CIs, 10,000 of them VMs. The delete request includes its anti-forgery token's round trip. Reads and saves of Servers in a loop while it runs: the longest, of how many (the longest of five alone in brackets).\n")
            .AppendLine(CultureInfo.InvariantCulture, $"Host: {Hardware()}; {sqlServer} in Docker; warm cache (seeded, then read before measuring); one read loop and one save loop at a time. The times are an order of magnitude on this host, not a bound; the escalation count and which requests wait don't depend on it.\n")
            .AppendLine("| Values | Delete request | Longest read of a Server | Longest save of a Server | Lock escalations on cmdb.Ci |")
            .AppendLine("|---:|---:|---:|---:|---:|");
        rows.ForEach(r => summary.AppendLine(r));
        TestContext.Current.TestOutputHelper?.WriteLine(summary.ToString());
        if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } file)
        {
            await File.AppendAllTextAsync(file, summary.ToString(), TestContext.Current.CancellationToken);
        }

        static async Task<double> TimeAsync(Func<Task<Response>> call)
        {
            var watch = Stopwatch.StartNew();
            var response = await call();
            Assert.True(response.Status == HttpStatusCode.OK, response.ToString());
            return watch.Elapsed.TotalMilliseconds;
        }

        static string Longest(List<double> times) =>
            times.Count == 0 ? "none" : string.Create(CultureInfo.InvariantCulture, $"{times.Max():F0} ms");

        // A Server as read, renamed, so the save writes (and takes the CI row's exclusive lock) as a real edit does.
        static Task<Response> SaveAsync(ApiClient client, Response read) =>
            client.PutAsync($"/api/v1/cis/{read.Value("id")}",
                new { name = read.Value("name") + ".", attributes = read.Body.GetProperty("attributes").Deserialize<Dictionary<string, object?>>() }, read.ETag);

        // The CPU model and memory the test sees, and the kernel (WSL2 shows in it), for reading the times.
        static string Hardware()
        {
            var cpu = File.Exists("/proc/cpuinfo")
                ? File.ReadLines("/proc/cpuinfo").FirstOrDefault(l => l.StartsWith("model name", StringComparison.Ordinal))?.Split(':', 2)[1].Trim()
                : Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
            var gib = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024.0 * 1024 * 1024);
            var kernel = File.Exists("/proc/sys/kernel/osrelease") ? $" (kernel {File.ReadAllText("/proc/sys/kernel/osrelease").Trim()})" : "";
            return string.Create(CultureInfo.InvariantCulture,
                $"{cpu ?? "unknown CPU"}, {Environment.ProcessorCount} logical CPUs and {gib:F0} GiB RAM seen, {RuntimeInformation.OSDescription}{kernel}");
        }

        static async Task<long> EscalationsAsync(SqlConnection connection)
        {
            await using var command = new SqlCommand(
                "SELECT ISNULL(SUM(index_lock_promotion_count), 0) FROM sys.dm_db_index_operational_stats(DB_ID(), OBJECT_ID(N'cmdb.Ci'), NULL, NULL);",
                connection);
            return Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken), CultureInfo.InvariantCulture);
        }
    }

    private static async Task<(string, string, double, double)> MeasureAsync(ApiClient client, string what, Func<int, string> path, double budget)
    {
        var times = new List<double>(Samples);
        for (var i = 0; i < Warmup + Samples; i++)
        {
            var watch = Stopwatch.StartNew();
            var response = await client.GetAsync(path(i));
            watch.Stop();
            Assert.True(response.Status == HttpStatusCode.OK, $"{what}: {response}");
            if (i >= Warmup)
            {
                times.Add(watch.Elapsed.TotalMilliseconds);
            }
        }

        times.Sort();
        return (what, path(0), times[(int)Math.Ceiling(0.95 * times.Count) - 1], budget);
    }
}

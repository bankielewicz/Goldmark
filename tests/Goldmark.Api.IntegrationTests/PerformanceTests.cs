using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using Goldmark.Api.IntegrationTests.Infrastructure;

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

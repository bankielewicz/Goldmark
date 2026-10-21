using Goldmark.Api.Data;
using Goldmark.Api.Hosting;
using Goldmark.Api.Security;

namespace Goldmark.Api.Audit;

/// <summary>
/// Screen 16, the audit log (US-07, DEC-041), for Admins and Auditors (US-04). Read-only, except that an
/// export writes its own entry (the filters and the row count) in the same transaction as the read, so
/// nobody takes a copy of the log without the log saying so. The export is a POST, so it needs the
/// anti-forgery header like every other request that writes.
/// </summary>
internal static class AuditEndpoints
{
    public static RouteGroupBuilder MapAuditEndpoints(this RouteGroupBuilder api)
    {
        var audit = api.MapGroup("/audit");

        api.MapGet("/audit", ListAsync)
            .RequireAccess(AccessPolicy.AuditReader)
            .WithDisplayName("Audit: list");

        audit.MapGet("/catalog", async (AuditRetention retention) =>
                Results.Ok(new { retentionDays = await retention.PeriodDaysAsync(), actions = AuditCatalog.Actions }))
            .RequireAccess(AccessPolicy.AuditReader)
            .WithDisplayName("Audit: catalog");

        audit.MapGet("/people", async (AuditLog log) => Results.Ok(new { people = await log.PeopleAsync() }))
            .RequireAccess(AccessPolicy.AuditReader)
            .WithDisplayName("Audit: people");

        audit.MapPost("/export", ExportAsync)
            .RequireAccess(AccessPolicy.AuditReader)
            .WithDisplayName("Audit: export CSV");

        return api;
    }

    private static async Task<IResult> ListAsync(HttpRequest request, AuditLog log)
    {
        var (query, limit, cursor, problem) = AuditQuery.ParsePage(request.Query);
        if (problem is not null)
        {
            return problem;
        }

        var (entries, next) = await log.PageAsync(query!, limit, cursor);
        return Results.Ok(new { entries, total = await log.CountAsync(query!), next });
    }

    /// <summary>The number of entries in an export, for the page's confirmation (the CSV has one row per entry).</summary>
    public const string EntriesHeader = "X-Goldmark-Entries";

    private static async Task<IResult> ExportAsync(HttpRequest request, AuditLog log, DbSession db, AuditTrail audit, TimeProvider time)
    {
        var (query, problem) = AuditQuery.Parse(request.Query);
        if (problem is not null)
        {
            return problem;
        }

        // The count refuses a large export before reading it. Entries written since can still take the
        // read past the limit, so the read takes one more than the limit and refuses too: an export is
        // never cut short.
        var count = await log.CountAsync(query!);
        if (count > AuditLog.ExportLimit)
        {
            return TooLarge(count);
        }

        var entries = await db.InTransactionAsync(async () =>
        {
            var exported = await log.ExportAsync(query!);
            if (exported.Count > AuditLog.ExportLimit)
            {
                return null;
            }

            await audit.WriteAsync(AuditActions.Exported, details: new { filters = query!.Describe(), entries = exported.Count });
            return exported;
        });
        if (entries is null)
        {
            return TooLarge(await log.CountAsync(query!));
        }

        request.HttpContext.Response.Headers[EntriesHeader] = entries.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Results.File(System.Text.Encoding.UTF8.GetBytes(AuditLog.ToCsv(entries)), "text/csv; charset=utf-8",
            $"goldmark-audit-{time.GetUtcNow():yyyyMMdd}.csv");
    }

    private static IResult TooLarge(long count) =>
        ApiProblems.Problem(StatusCodes.Status422UnprocessableEntity, "export-too-large",
            "Too many entries to export; narrow the time range or the filters.",
            new Dictionary<string, object?> { ["entries"] = count, ["limit"] = AuditLog.ExportLimit });
}

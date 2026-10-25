using System.Globalization;
using System.Text;
using Goldmark.Api.Audit;
using Goldmark.Api.Data;
using Goldmark.Api.Hosting;
using Goldmark.Api.Security;

namespace Goldmark.Api.Inventory;

/// <summary>
/// Screen 6, the graph explorer (US-40; DEC-046 items 4, 5 and 7), for everyone signed in. The read walks the
/// relationships around a CI (<see cref="GraphStore"/>). The CSV export holds every relationship the table view lists
/// and, as every export does (DEC-041 item 3), writes its own audit entry (the focus CI, the filters and the row count)
/// in the same transaction; a graph the read cut short is refused, never exported in part. The PNG is made in the
/// browser from the pixels on screen and isn't audited. The export is a POST, so it needs the anti-forgery header.
/// </summary>
internal static class GraphEndpoints
{
    public static RouteGroupBuilder MapGraphEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/graph", ReadAsync)
            .RequireAccess(AccessPolicy.SignedIn)
            .WithDisplayName("Graph: read");

        api.MapPost("/graph/export", ExportAsync)
            .RequireAccess(AccessPolicy.SignedIn)
            .WithDisplayName("Graph: export CSV");

        return api;
    }

    private static async Task<IResult> ReadAsync(HttpContext context, CiTypeCatalog catalog, GraphStore graph)
    {
        var types = await catalog.ListAsync();
        var relationshipTypes = await catalog.RelationshipTypesAsync();
        var (query, errors) = GraphQuery.Parse(context.Request.Query, types, relationshipTypes);
        if (query is null)
        {
            return CiEndpoints.Fields(errors);
        }

        return await graph.WalkAsync(query, types, relationshipTypes) is { } result ? Results.Ok(result) : CiNotFound();
    }

    private static async Task<IResult> ExportAsync(HttpContext context, CiTypeCatalog catalog, GraphStore graph, DbSession db, AuditTrail audit)
    {
        var types = await catalog.ListAsync();
        var relationshipTypes = await catalog.RelationshipTypesAsync();
        var (query, errors) = GraphQuery.Parse(context.Request.Query, types, relationshipTypes);
        if (query is null)
        {
            return CiEndpoints.Fields(errors);
        }

        return await db.InTransactionAsync(async () =>
        {
            if (await graph.WalkAsync(query, types, relationshipTypes) is not { } result)
            {
                return CiNotFound();
            }

            if (result.Truncated)
            {
                return ApiProblems.Problem(StatusCodes.Status422UnprocessableEntity, "export-too-large",
                    "Too many relationships to export; lower the depth, or filter by CI type or relationship type.",
                    new Dictionary<string, object?> { ["limit"] = GraphStore.MaxRelationships });
            }

            var focus = result.Nodes.First(n => n.Id == query.Ci);
            await audit.WriteAsync(AuditActions.RelationshipsExported, AuditTargetTypes.Ci, query.Ci,
                new { name = focus.Name, type = focus.Type, filters = query.Describe(), relationships = result.Relationships.Count });
            context.Response.Headers[AuditEndpoints.EntriesHeader] = result.Relationships.Count.ToString(CultureInfo.InvariantCulture);
            return Results.File(Csv.Utf8WithBom(ToCsv(result)), "text/csv; charset=utf-8",
                string.Create(CultureInfo.InvariantCulture, $"graph-{FileName(focus.Name)}-{query.Depth}-hops.csv"));
        });
    }

    /// <summary>The table view's columns (the GraphExport board), in the order the table lists the rows by default.</summary>
    internal static string ToCsv(GraphResult result)
    {
        var nodes = result.Nodes.ToDictionary(n => n.Id);
        var csv = new StringBuilder("From,From type,Relationship,To,To type,Redundancy group,Hops\r\n");
        foreach (var r in result.Relationships
            .OrderBy(r => r.Hops)
            .ThenBy(r => nodes[r.From].Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Type, StringComparer.Ordinal)
            .ThenBy(r => nodes[r.To].Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Id))
        {
            csv.AppendRow(
            [
                nodes[r.From].Name,
                nodes[r.From].Type,
                r.Type,
                nodes[r.To].Name,
                nodes[r.To].Type,
                r.RedundancyGroup ?? "",
                r.Hops.ToString(CultureInfo.InvariantCulture),
            ]);
        }

        return csv.ToString();
    }

    /// <summary>A CI's name as part of a file name: ASCII letters and digits, every other run of characters one hyphen.</summary>
    internal static string FileName(string name)
    {
        var file = new StringBuilder();
        foreach (var ch in name)
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                file.Append(ch);
            }
            else if (file.Length > 0 && file[^1] != '-')
            {
                file.Append('-');
            }
        }

        var text = file.ToString().TrimEnd('-');
        return text.Length == 0 ? "ci" : text[..Math.Min(text.Length, 100)];
    }

    private static IResult CiNotFound() => ApiProblems.Problem(StatusCodes.Status404NotFound, "not-found", "No such CI.");
}

using System.Globalization;
using Goldmark.Api.Audit;
using Goldmark.Api.Auth;
using Goldmark.Api.Data;
using Goldmark.Api.Hosting;
using Goldmark.Api.Security;
using Microsoft.Data.SqlClient;

namespace Goldmark.Api.Inventory;

/// <summary>
/// Relationships between CIs (screen 5's Relationships tab; US-11, FR-CM-6; DEC-046). Everyone signed in reads them;
/// Admins and Editors add, change and delete (<see cref="AccessPolicy.CiEditor"/>). A relationship's type and ends
/// never change: a change sets its redundancy group (item 2), and deleting it ends it, its history keeping it
/// (item 1). Several CIs in one Add are one request each (item 6). Every change is audited in its transaction; a
/// change or delete needs the relationship's ETag in If-Match (428 without it, 412 if someone else changed it
/// meanwhile). A relationship with a retired CI can still be changed and deleted, but none is added to one.
/// </summary>
internal static class RelationshipEndpoints
{
    private const string RelationshipTarget = AuditTargetTypes.Relationship;

    /// <summary>The built-in type whose dependency must be a Team or a Person, which its one To column can't say (0006).</summary>
    public const string OwnedBy = "owned_by";

    public static readonly IReadOnlyList<string> OwnerTypes = ["Team", "Person"];

    public static RouteGroupBuilder MapRelationshipEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/cis/{id:guid}/relationships", ListAsync)
            .RequireAccess(AccessPolicy.SignedIn)
            .WithDisplayName("Relationships: a CI's");

        api.MapGet("/cis/{id:guid}/redundancy-groups", GroupsAsync)
            .RequireAccess(AccessPolicy.SignedIn)
            .WithDisplayName("Relationships: a CI's redundancy groups");

        api.MapGet("/relationships/{id:guid}", GetAsync)
            .RequireAccess(AccessPolicy.SignedIn)
            .WithDisplayName("Relationships: get");

        api.MapPost("/relationships", CreateAsync)
            .RequireAccess(AccessPolicy.CiEditor)
            .WithDisplayName("Relationships: add");

        api.MapPut("/relationships/{id:guid}", ChangeAsync)
            .RequireAccess(AccessPolicy.CiEditor)
            .WithDisplayName("Relationships: change");

        api.MapDelete("/relationships/{id:guid}", DeleteAsync)
            .RequireAccess(AccessPolicy.CiEditor)
            .WithDisplayName("Relationships: delete");

        return api;
    }

    internal sealed record CreateRequest(string? Type, string? From, string? To, string? RedundancyGroup);

    /// <summary>The redundancy group to set; absent or empty means none.</summary>
    internal sealed record ChangeRequest(string? RedundancyGroup);

    /// <summary>
    /// The tab's sections: without <c>direction</c> the first page of both, each with its total; with <c>direction</c>
    /// (<c>dependencies</c> or <c>dependents</c>) one page of that section, after <c>cursor</c>.
    /// </summary>
    private static async Task<IResult> ListAsync(Guid id, HttpContext context, CiStore cis, RelationshipStore store)
    {
        var query = context.Request.Query;
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var direction = query["direction"].ToString();
        if (direction is not ("" or RelationshipDirections.Dependencies or RelationshipDirections.Dependents))
        {
            errors["direction"] = ["InvalidDirection"];
        }

        var limit = RelationshipStore.DefaultLimit;
        if (query["limit"].ToString() is { Length: > 0 } l
            && (!int.TryParse(l, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > RelationshipStore.MaxLimit))
        {
            errors["limit"] = ["InvalidLimit"];
        }

        RelationshipCursor? after = null;
        if (query["cursor"].ToString() is { Length: > 0 } c && (direction.Length == 0 || (after = RelationshipCursor.Decode(c, direction)) is null))
        {
            errors["cursor"] = ["InvalidCursor"];
        }

        if (errors.Count > 0)
        {
            return CiEndpoints.Fields(errors);
        }

        if (await cis.TypeIdAsync(id) is null)
        {
            return CiNotFound();
        }

        return direction.Length > 0
            ? Results.Ok(await store.SectionAsync(id, direction, limit, after))
            : Results.Ok(new
            {
                dependencies = await store.SectionAsync(id, RelationshipDirections.Dependencies, limit, null),
                dependents = await store.SectionAsync(id, RelationshipDirections.Dependents, limit, null),
            });
    }

    /// <summary>The panel's suggestions: the groups on the CI's relationships as the dependent (DEC-046 item 2).</summary>
    private static async Task<IResult> GroupsAsync(Guid id, CiStore cis, RelationshipStore store) =>
        await cis.TypeIdAsync(id) is null ? CiNotFound() : Results.Ok(new { groups = await store.GroupsAsync(id) });

    private static async Task<IResult> GetAsync(Guid id, HttpContext context, RelationshipStore store) =>
        await store.FindAsync(id) is { } relationship ? Detail(context, relationship) : NotFound();

    private static async Task<IResult> CreateAsync(CreateRequest request, HttpContext context, CiTypeCatalog catalog, CiStore cis,
        RelationshipStore store, DbSession db, AuditTrail audit)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var type = request.Type is null
            ? null
            : (await catalog.RelationshipTypesAsync()).FirstOrDefault(r => string.Equals(r.Name, request.Type, StringComparison.OrdinalIgnoreCase));
        if (type is null)
        {
            errors["type"] = [request.Type is null ? "Required" : "UnknownRelationshipType"];
        }

        var from = CiId(request.From, "from", errors);
        var to = CiId(request.To, "to", errors);
        if (from is not null && from == to)
        {
            errors["to"] = ["Self"];
        }

        var (group, groupError) = RedundancyGroups.Parse(request.RedundancyGroup);
        if (groupError is not null)
        {
            errors["redundancyGroup"] = [groupError];
        }

        if (errors.Count > 0)
        {
            return CiEndpoints.Fields(errors);
        }

        var editor = context.User.UserId()!.Value;
        try
        {
            return await CiEndpoints.RetryOnDeadlockAsync(() => db.InTransactionAsync(async () =>
            {
                // One lock order for every writer that touches these rows: the relationship type (shared), then the CIs'
                // types by ID (shared, as a CI save takes its own), then both CIs (shared, as a CI save's link check
                // takes the CIs it links to). An Admin's change to a type is met at the type, before this holds any CI;
                // a retire of either CI waits for this to commit, or this sees it retired.
                if (await catalog.LockRelationshipTypeForSaveAsync(type!.Id) is not { } locked)
                {
                    return ApiProblems.Field("type", "UnknownRelationshipType");
                }

                var typeIds = new[] { await cis.TypeIdAsync(from!.Value), await cis.TypeIdAsync(to!.Value) };
                if (typeIds.Any(t => t is null))
                {
                    return CiEndpoints.Fields(new Dictionary<string, string[]>(StringComparer.Ordinal)
                    {
                        [typeIds[0] is null ? "from" : "to"] = ["CiNotFound"],
                    });
                }

                foreach (var typeId in typeIds.Select(t => t!.Value).Distinct().Order())
                {
                    await catalog.LockForSaveAsync(typeId);
                }

                var ends = await cis.LinkedForSaveAsync([from.Value, to.Value]);
                var endErrors = new Dictionary<string, string[]>(StringComparer.Ordinal);
                foreach (var (field, ciId, allowed) in new[] { ("from", from.Value, locked.From), ("to", to.Value, locked.To) })
                {
                    var error = ends.TryGetValue(ciId, out var ci) switch
                    {
                        false => "CiNotFound",
                        true when ci!.LifecycleState == LifecycleStates.Retired => "CiRetired",
                        true when allowed is not null && ci!.Type != allowed => "WrongType",
                        true when field == "to" && locked.Name == OwnedBy && !OwnerTypes.Contains(ci!.Type, StringComparer.Ordinal) => "WrongType",
                        _ => null,
                    };
                    if (error is not null)
                    {
                        endErrors[field] = [error];
                    }
                }

                if (endErrors.Count > 0)
                {
                    return CiEndpoints.Fields(endErrors);
                }

                var id = Guid.CreateVersion7();
                await store.InsertAsync(id, from.Value, to.Value, locked.Id, group, editor);
                var added = (await store.FindAsync(id))!;
                await audit.WriteAsync(AuditActions.RelationshipAdded, RelationshipTarget, id, Recorded(added));
                context.Response.Headers.ETag = CiEndpoints.Quote(added.ETag);
                return Results.Created($"/api/v1/relationships/{id:D}", added);
            }));
        }
        catch (SqlException e) when (e.Number is UniqueIndexConflict or UniqueConstraintConflict)
        {
            return ApiProblems.Problem(StatusCodes.Status409Conflict, "exists", "That relationship already exists.");
        }
    }

    /// <summary>Changes the redundancy group (DEC-046 item 2); a save that changes nothing writes nothing.</summary>
    private static async Task<IResult> ChangeAsync(Guid id, ChangeRequest request, HttpContext context, RelationshipStore store, DbSession db,
        AuditTrail audit)
    {
        if (CiEndpoints.IfMatch(context) is not { } etag)
        {
            return IfMatchRequired();
        }

        var (group, groupError) = RedundancyGroups.Parse(request.RedundancyGroup);
        if (groupError is not null)
        {
            return ApiProblems.Field("redundancyGroup", groupError);
        }

        var editor = context.User.UserId()!.Value;
        try
        {
            return await CiEndpoints.RetryOnDeadlockAsync(() => db.InTransactionAsync(async () =>
            {
                if (await store.FindForUpdateAsync(id) is not { } row)
                {
                    return NotFound();
                }

                if (etag != row.ETag)
                {
                    return Changed();
                }

                // The text is stored as typed, so a change of case is a change.
                if (!string.Equals(group, row.RedundancyGroup, StringComparison.Ordinal))
                {
                    await store.UpdateAsync(id, group, editor);
                    await audit.WriteAsync(AuditActions.RelationshipChanged, RelationshipTarget, id, new
                    {
                        type = row.Type,
                        from = row.From.Name,
                        fromId = row.From.Id,
                        fromType = row.From.Type,
                        to = row.To.Name,
                        toId = row.To.Id,
                        toType = row.To.Type,
                        changed = RedundancyGroupField,
                        before = row.RedundancyGroup,
                        after = group,
                    });
                }

                return Detail(context, (await store.FindAsync(id))!);
            }));
        }
        catch (SqlException e) when (e.Number == SystemVersioningTimeConflict)
        {
            return Changed();
        }
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpContext context, RelationshipStore store, DbSession db, AuditTrail audit)
    {
        if (CiEndpoints.IfMatch(context) is not { } etag)
        {
            return IfMatchRequired();
        }

        try
        {
            return await CiEndpoints.RetryOnDeadlockAsync(() => db.InTransactionAsync(async () =>
            {
                if (await store.FindForUpdateAsync(id) is not { } row)
                {
                    return NotFound();
                }

                if (etag != row.ETag)
                {
                    return Changed();
                }

                await store.DeleteAsync(id);
                await audit.WriteAsync(AuditActions.RelationshipDeleted, RelationshipTarget, id, Recorded(row));
                return Results.NoContent();
            }));
        }
        catch (SqlException e) when (e.Number == SystemVersioningTimeConflict)
        {
            return Changed();
        }
    }

    /// <summary>
    /// What an entry records: the type and both CIs with their IDs, names and types, and the group. Screen 16 names a
    /// relationship from these, as a deleted one leaves no row to look up (<see cref="AuditLog"/>).
    /// </summary>
    private static object Recorded(RelationshipDetail r) => new
    {
        type = r.Type,
        from = r.From.Name,
        fromId = r.From.Id,
        fromType = r.From.Type,
        to = r.To.Name,
        toId = r.To.Id,
        toType = r.To.Type,
        redundancyGroup = r.RedundancyGroup,
    };

    private static readonly string[] RedundancyGroupField = ["RedundancyGroup"];

    /// <summary>A CI's ID as the request gives it, or an error under <paramref name="field"/>.</summary>
    private static Guid? CiId(string? value, string field, Dictionary<string, string[]> errors)
    {
        if (value is null)
        {
            errors[field] = ["Required"];
            return null;
        }

        if (!Guid.TryParseExact(value, "D", out var id))
        {
            errors[field] = ["InvalidId"];
            return null;
        }

        return id;
    }

    // SQL Server's 2601 and 2627: a unique index or constraint (UQ_Relationship: from, type, to) refused the row.
    private const int UniqueIndexConflict = 2601;
    private const int UniqueConstraintConflict = 2627;

    // SQL Server's 13535: this transaction began before the row's current version, so another one changed it.
    private const int SystemVersioningTimeConflict = 13535;

    private static IResult Detail(HttpContext context, RelationshipDetail detail)
    {
        context.Response.Headers.ETag = CiEndpoints.Quote(detail.ETag);
        return Results.Ok(detail);
    }

    private static IResult NotFound() => ApiProblems.Problem(StatusCodes.Status404NotFound, "not-found", "No such relationship.");

    private static IResult CiNotFound() => ApiProblems.Problem(StatusCodes.Status404NotFound, "not-found", "No such CI.");

    private static IResult IfMatchRequired() =>
        ApiProblems.Problem(StatusCodes.Status428PreconditionRequired, "if-match-required", "Send the relationship's ETag in If-Match.");

    private static IResult Changed() =>
        ApiProblems.Problem(StatusCodes.Status412PreconditionFailed, "changed", "Someone else changed this relationship meanwhile. Reload and try again.");
}

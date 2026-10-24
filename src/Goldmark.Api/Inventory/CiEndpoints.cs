using System.Text.Json;
using Goldmark.Api.Audit;
using Goldmark.Api.Auth;
using Goldmark.Api.Data;
using Goldmark.Api.Hosting;
using Goldmark.Api.Security;
using Microsoft.Data.SqlClient;

namespace Goldmark.Api.Inventory;

/// <summary>
/// CIs (screens 4 and 5; US-11, US-53; DEC-044); their types are <see cref="TypeEndpoints"/>. Everyone
/// signed in reads; Admins and Editors create, change, retire and restore (<see cref="AccessPolicy.CiEditor"/>).
/// A save locks its type and reads its definitions inside the transaction (<see cref="CiTypeCatalog"/>), so an
/// Admin's change to the type can't slip in between; no new CI of a hidden type (DEC-045 item 5). Every change is
/// audited in its transaction. A change needs the CI's ETag in If-Match: HTTP 428 without it, 412 if
/// someone else saved meanwhile; a retired CI is changed only by restoring it (409 <c>retired</c>).
/// CIs are never deleted (US-12: retired CIs stay, with their history).
/// </summary>
internal static class CiEndpoints
{
    private const string CiTarget = AuditTargetTypes.Ci;

    public static RouteGroupBuilder MapCiEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/cis", SearchAsync)
            .RequireAccess(AccessPolicy.SignedIn)
            .WithDisplayName("CIs: search");

        api.MapGet("/cis/{id:guid}", GetAsync)
            .RequireAccess(AccessPolicy.SignedIn)
            .WithDisplayName("CIs: get");

        api.MapPost("/cis", CreateAsync)
            .RequireAccess(AccessPolicy.CiEditor)
            .WithDisplayName("CIs: create");

        api.MapPut("/cis/{id:guid}", UpdateAsync)
            .RequireAccess(AccessPolicy.CiEditor)
            .WithDisplayName("CIs: change");

        api.MapPost("/cis/{id:guid}/retire", (Guid id, HttpContext context, IServiceProvider services) =>
                SetLifecycleAsync(id, context, services, LifecycleStates.Retired))
            .RequireAccess(AccessPolicy.CiEditor)
            .WithDisplayName("CIs: retire");

        api.MapPost("/cis/{id:guid}/restore", (Guid id, HttpContext context, IServiceProvider services) =>
                SetLifecycleAsync(id, context, services, LifecycleStates.Active))
            .RequireAccess(AccessPolicy.CiEditor)
            .WithDisplayName("CIs: restore");

        return api;
    }

    internal sealed record CreateRequest(string? Type, string? Name, JsonElement? Attributes);

    internal sealed record ChangeRequest(string? Name, JsonElement? Attributes);

    private static async Task<IResult> SearchAsync(HttpContext context, CiTypeCatalog catalog, CiStore store)
    {
        var (search, errors) = CiSearch.Parse(context.Request.Query, await catalog.ListAsync());
        return search is null ? Fields(errors) : Results.Ok(await store.SearchAsync(search));
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext context, CiTypeCatalog catalog, CiStore store)
    {
        if (await store.FindAsync(id) is not { } row)
        {
            return NotFound();
        }

        return Detail(context, await store.DetailAsync(row, TypeOf(await catalog.ListAsync(), row)));
    }

    private static async Task<IResult> CreateAsync(CreateRequest request, HttpContext context, CiTypeCatalog catalog, CiStore store,
        DbSession db, AuditTrail audit)
    {
        if (CiTypeCatalog.Find(await catalog.ListAsync(), request.Type) is not { } found)
        {
            return ApiProblems.Field("type", request.Type is null ? "Required" : "UnknownType");
        }

        var editor = context.User.UserId()!.Value;
        return await RetryOnDeadlockAsync(() => db.InTransactionAsync(async () =>
        {
            if (await catalog.LockForSaveAsync(found.Id) is not { } type)
            {
                return ApiProblems.Field("type", "UnknownType");
            }

            if (type.Hidden)
            {
                return ApiProblems.Field("type", "HiddenType");
            }

            var (name, check, errors) = await CheckAsync(store, type, request.Name, request.Attributes, stored: null, self: null);
            if (errors.Count > 0)
            {
                return Fields(errors);
            }

            var id = Guid.CreateVersion7();
            await store.InsertAsync(id, type.Id, name, check.Values.ToJsonString(), editor);
            await audit.WriteAsync(AuditActions.CiCreated, CiTarget, id, new { type = type.Name, name });
            var detail = await store.DetailAsync((await store.FindAsync(id))!, type);
            context.Response.Headers.ETag = Quote(detail.ETag);
            return Results.Created($"/api/v1/cis/{id:D}", detail);
        }));
    }

    private static async Task<IResult> UpdateAsync(Guid id, ChangeRequest request, HttpContext context, CiTypeCatalog catalog, CiStore store,
        DbSession db, AuditTrail audit)
    {
        if (IfMatch(context) is not { } etag)
        {
            return IfMatchRequired();
        }

        var editor = context.User.UserId()!.Value;
        try
        {
            return await RetryOnDeadlockAsync(() => db.InTransactionAsync(async () =>
            {
                if (await LockTypeThenCiAsync(id, catalog, store) is not { } locked)
                {
                    return NotFound();
                }

                var (type, row) = locked;

                // Before the version: a CI retired meanwhile says so rather than "changed" (screen 5's panel).
                if (row.LifecycleState == LifecycleStates.Retired)
                {
                    return ApiProblems.Problem(StatusCodes.Status409Conflict, "retired", "This CI is retired. Restore it to make changes.");
                }

                if (etag != CiStore.Stamp(row.ValidFrom))
                {
                    return Changed();
                }

                // PUT replaces every value (DEC-044 item 11), so a request without them is refused rather than
                // read as "unset them all"; an empty object still clears every value (item 13).
                if (request.Attributes is null or { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined })
                {
                    return ApiProblems.Field("attributes", "Required");
                }

                var before = AttributeValues.Parse(row.Attributes);
                var (name, check, errors) = await CheckAsync(store, type, request.Name, request.Attributes, before, self: id);
                if (errors.Count > 0)
                {
                    return Fields(errors);
                }

                var fields = (name == row.Name ? [] : NameField).Concat(AttributeValues.Changed(type, before, check.Values)).ToList();
                if (fields.Count > 0)
                {
                    await store.UpdateAsync(id, name, check.Values.ToJsonString(), editor);
                    await audit.WriteAsync(AuditActions.CiChanged, CiTarget, id, new { type = type.Name, name, fields });
                }

                return Detail(context, await store.DetailAsync((await store.FindAsync(id))!, type));
            }));
        }
        catch (SqlException e) when (e.Number == SystemVersioningTimeConflict)
        {
            return Changed();
        }
    }

    private static async Task<IResult> SetLifecycleAsync(Guid id, HttpContext context, IServiceProvider services, string state)
    {
        if (IfMatch(context) is not { } etag)
        {
            return IfMatchRequired();
        }

        var catalog = services.GetRequiredService<CiTypeCatalog>();
        var store = services.GetRequiredService<CiStore>();
        var db = services.GetRequiredService<DbSession>();
        var audit = services.GetRequiredService<AuditTrail>();
        var editor = context.User.UserId()!.Value;
        try
        {
            return await RetryOnDeadlockAsync(() => db.InTransactionAsync(async () =>
            {
                if (await LockTypeThenCiAsync(id, catalog, store) is not { } locked)
                {
                    return NotFound();
                }

                var (type, row) = locked;

                if (etag != CiStore.Stamp(row.ValidFrom))
                {
                    return Changed();
                }

                var retiring = state == LifecycleStates.Retired;
                if ((row.LifecycleState == LifecycleStates.Retired) == retiring)
                {
                    return retiring
                        ? ApiProblems.Problem(StatusCodes.Status409Conflict, "retired", "This CI is already retired.")
                        : ApiProblems.Problem(StatusCodes.Status409Conflict, "not-retired", "This CI isn't retired.");
                }

                await store.SetLifecycleAsync(id, state, editor);
                await audit.WriteAsync(retiring ? AuditActions.CiRetired : AuditActions.CiRestored, CiTarget, id, new { type = type.Name, name = row.Name });
                return Detail(context, await store.DetailAsync((await store.FindAsync(id))!, type));
            }));
        }
        catch (SqlException e) when (e.Number == SystemVersioningTimeConflict)
        {
            return Changed();
        }
    }

    /// <summary>
    /// The name and values checked against the type, with each new link to another CI looked up: it must
    /// exist, not be retired and, if the attribute names one, have that type. A link left as stored
    /// isn't rechecked, so retiring a CI doesn't block saving the CIs that point at it.
    /// </summary>
    private static async Task<(string Name, AttributeCheck Check, Dictionary<string, string[]> Errors)> CheckAsync(CiStore store,
        CiTypeInfo type, string? name, JsonElement? attributes, System.Text.Json.Nodes.JsonObject? stored, Guid? self)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (AttributeValues.NameError(name) is { } nameError)
        {
            errors["name"] = [nameError];
        }

        var check = AttributeValues.Check(type, attributes);
        foreach (var (field, codes) in check.Errors)
        {
            errors[field] = codes;
        }

        var changedLinks = check.Links.Where(l => stored?[l.Key]?.GetValue<string>() != l.Value.ToString("D")).ToList();
        var linked = await store.LinkedForSaveAsync(changedLinks.Select(l => l.Value));
        foreach (var (attribute, id) in changedLinks)
        {
            var refType = type.Attributes.First(a => a.Name == attribute).RefType;
            var error = linked.TryGetValue(id, out var ci) switch
            {
                _ when id == self => "LinkedCiSelf",
                false => "LinkedCiNotFound",
                true when ci!.LifecycleState == LifecycleStates.Retired => "LinkedCiRetired",
                true when refType is not null && ci!.Type != refType => "LinkedCiWrongType",
                _ => null,
            };
            if (error is not null)
            {
                errors[AttributeValues.Field(attribute)] = [error];
            }
        }

        return (name?.Trim() ?? "", check, errors);
    }

    private static readonly string[] NameField = ["Name"];

    // SQL Server's 1205: this transaction was chosen as a deadlock victim and rolled back.
    internal const int Deadlock = 1205;

    /// <summary>
    /// The CI and its type, locked for a change (call inside a transaction): the type's row first (shared), then the
    /// CI's. That is the order an Admin's change to the type takes them (<see cref="CiTypeCatalog.LockForChangeAsync"/>
    /// holds the type, then rewrites its CIs), so the two wait for each other; a change holding the CI while waiting
    /// for its type would deadlock with it. Null when there's no such CI.
    /// </summary>
    private static async Task<(CiTypeInfo Type, CiStore.CiRow Row)?> LockTypeThenCiAsync(Guid id, CiTypeCatalog catalog, CiStore store) =>
        await store.TypeIdAsync(id) is { } typeId && await catalog.LockForSaveAsync(typeId) is { } type && await store.FindForUpdateAsync(id) is { } row
            ? (type, row)
            : null;

    /// <summary>
    /// A change to a CI that SQL Server chose as a deadlock victim runs once more; the other transaction has committed
    /// by then. Two saves that each newly link to the other's CI hold the link check's shared locks (DEC-044 item 13),
    /// and a save's link check reads a linked CI's type after locking that CI, which can meet an Admin's change to that
    /// type. A second loss answers 412, as a change meanwhile does, and the screen reloads.
    /// </summary>
    internal static async Task<IResult> RetryOnDeadlockAsync(Func<Task<IResult>> save)
    {
        try
        {
            return await save();
        }
        catch (SqlException e) when (e.Number == Deadlock)
        {
            try
            {
                return await save();
            }
            catch (SqlException again) when (again.Number == Deadlock)
            {
                return Changed();
            }
        }
    }

    // SQL Server's 13535: this transaction began before the row's current version, so another one changed it.
    private const int SystemVersioningTimeConflict = 13535;

    private static CiTypeInfo TypeOf(IReadOnlyList<CiTypeInfo> types, CiStore.CiRow row) => types.First(t => t.Id == row.CiTypeId);

    private static IResult Detail(HttpContext context, CiDetail detail)
    {
        context.Response.Headers.ETag = Quote(detail.ETag);
        return Results.Ok(detail);
    }

    internal static IResult Fields(Dictionary<string, string[]> errors) =>
        Results.ValidationProblem(errors, title: "Some values are not valid.", extensions: new Dictionary<string, object?> { ["code"] = "validation" });

    internal static string? IfMatch(HttpContext context)
    {
        var value = context.Request.Headers.IfMatch.ToString().Trim();
        return value.Length > 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : null;
    }

    internal static string Quote(string etag) => $"\"{etag}\"";

    private static IResult NotFound() => ApiProblems.Problem(StatusCodes.Status404NotFound, "not-found", "No such CI.");

    private static IResult IfMatchRequired() =>
        ApiProblems.Problem(StatusCodes.Status428PreconditionRequired, "if-match-required", "Send the CI's ETag in If-Match.");

    private static IResult Changed() =>
        ApiProblems.Problem(StatusCodes.Status412PreconditionFailed, "changed", "Someone else changed this CI meanwhile. Reload and try again.");
}

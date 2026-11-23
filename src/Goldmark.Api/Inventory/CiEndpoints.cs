using System.Globalization;
using System.Text.Json;
using Goldmark.Api.Audit;
using Goldmark.Api.Data;
using Goldmark.Api.Hosting;
using Goldmark.Api.Security;
using Goldmark.Api.Tokens;
using Microsoft.Data.SqlClient;
using Microsoft.Net.Http.Headers;

namespace Goldmark.Api.Inventory;

/// <summary>
/// CIs (screens 4 and 5; US-11, US-53; DEC-044); their types are <see cref="TypeEndpoints"/>. Everyone
/// signed in reads; Admins and Editors create, change, retire and restore (<see cref="AccessPolicy.CiEditor"/>);
/// an API token reads with <c>cis:read</c> and changes with <c>cis:write</c>, as itself (DEC-048).
/// A save locks its type and reads its definitions inside the transaction (<see cref="CiTypeCatalog"/>), so an
/// Admin's change to the type can't slip in between; no new CI of a hidden type (DEC-045 item 5). Every change is
/// audited in its transaction. PUT replaces the name and every value; PATCH, a JSON Merge Patch (#48, DEC-048), changes
/// what it names. A change needs the CI's ETag in If-Match: HTTP 428 without it, 412 if
/// someone else saved meanwhile; a retired CI is changed only by restoring it (409 <c>retired</c>).
/// CIs are never deleted (US-12: retired CIs stay, with their history).
/// </summary>
internal static class CiEndpoints
{
    private const string CiTarget = AuditTargetTypes.Ci;

    public static RouteGroupBuilder MapCiEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/cis", SearchAsync)
            .RequireAccess(AccessPolicy.SignedIn, TokenScopes.CisRead)
            .WithDisplayName("CIs: search");

        api.MapGet("/cis/{id:guid}", GetAsync)
            .RequireAccess(AccessPolicy.SignedIn, TokenScopes.CisRead)
            .WithDisplayName("CIs: get");

        api.MapGet("/cis/{id:guid}/history", HistoryAsync)
            .RequireAccess(AccessPolicy.SignedIn, TokenScopes.CisRead)
            .WithDisplayName("CIs: history");

        api.MapPost("/cis", CreateAsync)
            .RequireAccess(AccessPolicy.CiEditor, TokenScopes.CisWrite)
            .WithDisplayName("CIs: create");

        api.MapPut("/cis/{id:guid}", UpdateAsync)
            .RequireAccess(AccessPolicy.CiEditor, TokenScopes.CisWrite)
            .WithDisplayName("CIs: change");

        api.MapPatch("/cis/{id:guid}", PatchAsync)
            .RequireAccess(AccessPolicy.CiEditor, TokenScopes.CisWrite)
            .WithDisplayName("CIs: partial save");

        api.MapPost("/cis/{id:guid}/retire", (Guid id, HttpContext context, IServiceProvider services) =>
                SetLifecycleAsync(id, context, services, LifecycleStates.Retired))
            .RequireAccess(AccessPolicy.CiEditor, TokenScopes.CisWrite)
            .WithDisplayName("CIs: retire");

        api.MapPost("/cis/{id:guid}/restore", (Guid id, HttpContext context, IServiceProvider services) =>
                SetLifecycleAsync(id, context, services, LifecycleStates.Active))
            .RequireAccess(AccessPolicy.CiEditor, TokenScopes.CisWrite)
            .WithDisplayName("CIs: restore");

        return api;
    }

    internal sealed record CreateRequest(string? Type, string? Name, JsonElement? Attributes);

    internal sealed record ChangeRequest(string? Name, JsonElement? Attributes);

    /// <summary>The search, now or with <c>asOf</c> as it would have answered then (US-42): the CIs, names and types of that moment.</summary>
    private static async Task<IResult> SearchAsync(HttpContext context, CiTypeCatalog catalog, CiStore store)
    {
        var (asOf, refusal) = await AsOf.ReadAsync(context);
        if (refusal is not null)
        {
            return refusal;
        }

        var (search, errors) = CiSearch.Parse(context.Request.Query, await catalog.ListAsync(asOf), asOf);
        return search is null ? Fields(errors) : Results.Ok(await store.SearchAsync(search));
    }

    /// <summary>
    /// The CI, now or with <c>asOf</c> as it was then: its values under the attributes its type had then, and the
    /// CIs it linked to as they were. A past version is read-only, so it carries no ETag header.
    /// </summary>
    private static async Task<IResult> GetAsync(Guid id, HttpContext context, CiTypeCatalog catalog, CiStore store)
    {
        var (asOf, refusal) = await AsOf.ReadAsync(context);
        if (refusal is not null)
        {
            return refusal;
        }

        if (await store.FindAsync(id, asOf) is not { } row)
        {
            return await AsOf.NotThenAsync(store, id, asOf) ?? NotFound();
        }

        var detail = await store.DetailAsync(row, TypeOf(await catalog.ListAsync(asOf), row), asOf);
        return asOf is null ? Detail(context, detail) : Results.Ok(detail);
    }

    /// <summary>
    /// The CI's history (US-43), newest first: each version of the CI as created, changed, retired or restored with
    /// its differences, and each relationship at either end as added, changed and ended, each with who did it. It is
    /// the whole past, so it takes no <c>asOf</c>; an entry's <c>at</c> is the <c>asOf</c> that shows the CI from then.
    /// </summary>
    private static async Task<IResult> HistoryAsync(Guid id, HttpContext context, CiHistoryStore history)
    {
        if (AsOf.RefuseIfAsked(context) is { } refusal)
        {
            return refusal;
        }

        var query = context.Request.Query;
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var limit = CiHistoryStore.DefaultLimit;
        if (query["limit"].ToString() is { Length: > 0 } l
            && (!int.TryParse(l, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > CiHistoryStore.MaxLimit))
        {
            errors["limit"] = ["InvalidLimit"];
        }

        (DateTime At, string Tie)? after = null;
        if (query["cursor"].ToString() is { Length: > 0 } c && (after = HistoryCursor.Decode(c)) is null)
        {
            errors["cursor"] = ["InvalidCursor"];
        }

        if (errors.Count > 0)
        {
            return Fields(errors);
        }

        return await history.PageAsync(id, limit, after) is { } page ? Results.Ok(page) : NotFound();
    }

    private static async Task<IResult> CreateAsync(CreateRequest request, HttpContext context, CiTypeCatalog catalog, CiStore store,
        DbSession db, AuditTrail audit)
    {
        if (CiTypeCatalog.Find(await catalog.ListAsync(), request.Type) is not { } found)
        {
            return ApiProblems.Field("type", request.Type is null ? "Required" : "UnknownType");
        }

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
            await store.InsertAsync(id, type.Id, name, check.Values.ToJsonString());
            await audit.WriteAsync(AuditActions.CiCreated, CiTarget, id, new { type = type.Name, name });
            var detail = await store.DetailAsync((await store.FindAsync(id))!, type);
            context.Response.Headers.ETag = Quote(detail.ETag);
            return Results.Created($"/api/v1/cis/{id:D}", detail);
        }));
    }

    private static Task<IResult> UpdateAsync(Guid id, ChangeRequest request, HttpContext context, CiTypeCatalog catalog, CiStore store,
        DbSession db, AuditTrail audit) =>
        ChangeAsync(id, context, catalog, store, db, audit, (_, _) =>
            // PUT replaces every value (DEC-044 item 11), so a request without them is refused rather than
            // read as "unset them all"; an empty object still clears every value (item 13).
            request.Attributes is null or { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined }
                ? RequestedChange.Refused(ApiProblems.Field("attributes", "Required"))
                : new RequestedChange(request.Name, request.Attributes, NoErrors));

    /// <summary>
    /// A partial save (#48; DEC-044 item 11, DEC-048): a JSON Merge Patch of the name and values (<see cref="CiMergePatch"/>),
    /// then PUT's path exactly. Only <c>application/merge-patch+json</c> is taken (415 with <c>Accept-Patch</c>
    /// otherwise), and only a JSON object (400 <c>invalid-patch</c>). The body is read here, after the anti-forgery check.
    /// </summary>
    private static async Task<IResult> PatchAsync(Guid id, HttpContext context, CiTypeCatalog catalog, CiStore store, DbSession db, AuditTrail audit)
    {
        if (!IsMergePatch(context.Request))
        {
            context.Response.Headers["Accept-Patch"] = CiMergePatch.MediaType;
            return ApiProblems.Problem(StatusCodes.Status415UnsupportedMediaType, "unsupported-media-type",
                "Send the changes as a JSON Merge Patch, with Content-Type application/merge-patch+json.");
        }

        JsonElement patch;
        try
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            patch = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return InvalidPatch();
        }

        if (patch.ValueKind != JsonValueKind.Object)
        {
            return InvalidPatch();
        }

        return await ChangeAsync(id, context, catalog, store, db, audit, (row, stored) =>
        {
            var (name, attributes, errors) = CiMergePatch.Apply(patch, row.Name, stored);
            return new RequestedChange(name, attributes, errors);
        });
    }

    /// <summary>
    /// A change to a CI's name and values, PUT's and PATCH's alike (DEC-044 items 11 to 13): If-Match (428 without it), the
    /// type's lock and then the CI's, a retired CI refused (409 <c>retired</c>), a version changed meanwhile 412, the values
    /// checked against the type together with the request's own field errors, and the save audited <c>ci.changed</c> with
    /// the fields it changed (nothing is written when none did); a deadlock victim runs once more. <paramref name="requested"/>
    /// turns the CI as stored, and its stored values, into what the request asks for.
    /// </summary>
    private static async Task<IResult> ChangeAsync(Guid id, HttpContext context, CiTypeCatalog catalog, CiStore store, DbSession db,
        AuditTrail audit, Func<CiStore.CiRow, System.Text.Json.Nodes.JsonObject, RequestedChange> requested)
    {
        if (IfMatch(context) is not { } etag)
        {
            return IfMatchRequired();
        }

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

                var before = AttributeValues.Parse(row.Attributes);
                var change = requested(row, before);
                if (change.Refusal is not null)
                {
                    return change.Refusal;
                }

                var (name, check, errors) = await CheckAsync(store, type, change.Name, change.Attributes, before, self: id);
                foreach (var (field, codes) in change.Errors)
                {
                    errors[field] = codes;
                }

                if (errors.Count > 0)
                {
                    return Fields(errors);
                }

                var fields = (name == row.Name ? [] : NameField).Concat(AttributeValues.Changed(type, before, check.Values)).ToList();
                if (fields.Count > 0)
                {
                    await store.UpdateAsync(id, name, check.Values.ToJsonString());
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

    /// <summary>What a change asks for, from the CI as stored: its name and values, with the request's own field errors; or its refusal.</summary>
    private sealed record RequestedChange(string? Name, JsonElement? Attributes, IReadOnlyDictionary<string, string[]> Errors, IResult? Refusal = null)
    {
        public static RequestedChange Refused(IResult refusal) => new(null, null, NoErrors, refusal);
    }

    private static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();

    /// <summary>The merge patch media type, in any case, in UTF-8 if a charset is named (RFC 8259: JSON between systems is UTF-8).</summary>
    private static bool IsMergePatch(HttpRequest request) =>
        MediaTypeHeaderValue.TryParse(request.ContentType, out var type)
        && type.MediaType.Equals(CiMergePatch.MediaType, StringComparison.OrdinalIgnoreCase)
        && (type.Charset.Length == 0 || type.Charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase));

    private static IResult InvalidPatch() =>
        ApiProblems.Problem(StatusCodes.Status400BadRequest, "invalid-patch", "Send a JSON object with the fields to change (a JSON Merge Patch).");

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

                await store.SetLifecycleAsync(id, state);
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

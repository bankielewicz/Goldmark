using System.Text.Json;
using System.Text.RegularExpressions;
using Goldmark.Api.Audit;
using Goldmark.Api.Auth;
using Goldmark.Api.Data;
using Goldmark.Api.Hosting;
using Goldmark.Api.Security;
using Microsoft.Data.SqlClient;

namespace Goldmark.Api.Inventory;

/// <summary>
/// CIs and their types (screens 4 and 5; US-10, US-11, US-53; DEC-044). Everyone signed in reads;
/// Admins and Editors create, change, retire and restore (<see cref="AccessPolicy.CiEditor"/>); only an
/// Admin adds a custom attribute to a type, and only adds (DEC-044 items 4 and 8). Every change is
/// audited in its transaction. A change needs the CI's ETag in If-Match: HTTP 428 without it, 412 if
/// someone else saved meanwhile; a retired CI is changed only by restoring it (409 <c>retired</c>).
/// CIs are never deleted (US-12: retired CIs stay, with their history).
/// </summary>
internal static partial class CiEndpoints
{
    private const string CiTarget = AuditTargetTypes.Ci;
    private const string CiTypeTarget = AuditTargetTypes.CiType;

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

        api.MapGet("/types", ListTypesAsync)
            .RequireAccess(AccessPolicy.SignedIn)
            .WithDisplayName("Types: list");

        api.MapPost("/types/{type}/attributes", AddAttributeAsync)
            .RequireAccess(AccessPolicy.Admin)
            .WithDisplayName("Types: add an attribute");

        return api;
    }

    internal sealed record CreateRequest(string? Type, string? Name, JsonElement? Attributes);

    internal sealed record ChangeRequest(string? Name, JsonElement? Attributes);

    internal sealed record AttributeRequest(string? Name, string? Label, string? DataType, bool? Required, IReadOnlyList<string>? Choices,
        string? RefType);

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
        var types = await catalog.ListAsync();
        if (CiTypeCatalog.Find(types, request.Type) is not { } type)
        {
            return ApiProblems.Field("type", request.Type is null ? "Required" : "UnknownType");
        }

        var editor = context.User.UserId()!.Value;
        return await db.InTransactionAsync(async () =>
        {
            var (name, check, errors) = await CheckAsync(store, type, request.Name, request.Attributes, stored: null);
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
        });
    }

    private static async Task<IResult> UpdateAsync(Guid id, ChangeRequest request, HttpContext context, CiTypeCatalog catalog, CiStore store,
        DbSession db, AuditTrail audit)
    {
        if (IfMatch(context) is not { } etag)
        {
            return IfMatchRequired();
        }

        var types = await catalog.ListAsync();
        var editor = context.User.UserId()!.Value;
        try
        {
            return await db.InTransactionAsync(async () =>
            {
                if (await store.FindForUpdateAsync(id) is not { } row)
                {
                    return NotFound();
                }

                // Before the version: a CI retired meanwhile says so rather than "changed" (screen 5's panel).
                if (row.LifecycleState == LifecycleStates.Retired)
                {
                    return ApiProblems.Problem(StatusCodes.Status409Conflict, "retired", "This CI is retired. Restore it to make changes.");
                }

                if (etag != CiStore.Stamp(row.ValidFrom))
                {
                    return Changed();
                }

                var type = TypeOf(types, row);
                var before = AttributeValues.Parse(row.Attributes);
                var (name, check, errors) = await CheckAsync(store, type, request.Name, request.Attributes, before);
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
            });
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
        var types = await catalog.ListAsync();
        var editor = context.User.UserId()!.Value;
        try
        {
            return await db.InTransactionAsync(async () =>
            {
                if (await store.FindForUpdateAsync(id) is not { } row)
                {
                    return NotFound();
                }

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

                var type = TypeOf(types, row);
                await store.SetLifecycleAsync(id, state, editor);
                await audit.WriteAsync(retiring ? AuditActions.CiRetired : AuditActions.CiRestored, CiTarget, id, new { type = type.Name, name = row.Name });
                return Detail(context, await store.DetailAsync((await store.FindAsync(id))!, type));
            });
        }
        catch (SqlException e) when (e.Number == SystemVersioningTimeConflict)
        {
            return Changed();
        }
    }

    private static async Task<IResult> ListTypesAsync(CiTypeCatalog catalog) =>
        Results.Ok(new
        {
            types = (await catalog.ListAsync()).Select(t => new
            {
                t.Id,
                t.Name,
                t.Category,
                t.Icon,
                t.BuiltIn,
                attributes = t.Attributes.Select(Attribute),
            }),
            relationshipTypes = await catalog.RelationshipTypesAsync(),
        });

    private static async Task<IResult> AddAttributeAsync(string type, AttributeRequest request, HttpContext context, CiTypeCatalog catalog,
        DbSession db, AuditTrail audit)
    {
        var types = await catalog.ListAsync();
        if (CiTypeCatalog.Find(types, type) is not { } target)
        {
            return ApiProblems.Problem(StatusCodes.Status404NotFound, "not-found", "No such CI type.");
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var name = request.Name?.Trim() ?? "";
        if (!AttributeName().IsMatch(name))
        {
            errors["name"] = [name.Length == 0 ? "Required" : "InvalidName"];
        }
        else if (target.Attributes.Any(a => a.Name == name))
        {
            errors["name"] = ["DuplicateName"];
        }

        var label = request.Label?.Trim() ?? "";
        if (label.Length == 0 || label.Length > 100 || label.Any(char.IsControl))
        {
            errors["label"] = [label.Length == 0 ? "Required" : "InvalidLabel"];
        }
        else if (target.Attributes.Any(a => string.Equals(a.Label, label, StringComparison.OrdinalIgnoreCase)))
        {
            errors["label"] = ["DuplicateLabel"];
        }

        var dataType = AttributeDataTypes.FromApi(request.DataType);
        if (dataType is null)
        {
            errors["dataType"] = [request.DataType is null ? "Required" : "InvalidDataType"];
        }

        var choices = request.Choices?.Select(c => c?.Trim() ?? "").ToList();
        if (dataType == AttributeDataTypes.Enum
            && (choices is null || choices.Count is 0 or > 100 || choices.Any(c => c.Length is 0 or > 100 || c.Any(char.IsControl))
                || choices.Distinct(StringComparer.Ordinal).Count() != choices.Count))
        {
            errors["choices"] = ["InvalidChoices"];
        }
        else if (dataType != AttributeDataTypes.Enum && choices is not null)
        {
            errors["choices"] = ["ChoicesNotAllowed"];
        }

        CiTypeInfo? refType = null;
        if (request.RefType is not null && dataType != AttributeDataTypes.CiRef)
        {
            errors["refType"] = ["RefTypeNotAllowed"];
        }
        else if (request.RefType is not null && (refType = CiTypeCatalog.Find(types, request.RefType)) is null)
        {
            errors["refType"] = ["UnknownType"];
        }

        if (errors.Count > 0)
        {
            return Fields(errors);
        }

        var required = request.Required ?? false;
        try
        {
            return await db.InTransactionAsync(async () =>
            {
                var id = await catalog.AddAttributeAsync(target.Id, name, label, dataType!, required,
                    dataType == AttributeDataTypes.Enum ? choices : null, refType?.Id, context.User.UserId()!.Value);
                await audit.WriteAsync(AuditActions.AttributeAdded, CiTypeTarget, target.Id,
                    new { type = target.Name, attribute = label, name, dataType = AttributeDataTypes.ToApi(dataType!), required });
                var added = new AttributeDefinition(id, name, label, dataType!, required, dataType == AttributeDataTypes.Enum ? choices : null,
                    refType?.Name, BuiltIn: false);
                return Results.Created($"/api/v1/types/{target.Name}", Attribute(added));
            });
        }
        catch (SqlException e) when (e.Number is 2601 or 2627)
        {
            // Someone added the same name or label meanwhile.
            return e.Message.Contains("UQ_AttributeDef_Label", StringComparison.Ordinal)
                ? ApiProblems.Field("label", "DuplicateLabel")
                : ApiProblems.Field("name", "DuplicateName");
        }
    }

    /// <summary>
    /// The name and values checked against the type, with each new link to another CI looked up: it must
    /// exist, not be retired and, if the attribute names one, have that type. A link left as stored
    /// isn't rechecked, so retiring a CI doesn't block saving the CIs that point at it.
    /// </summary>
    private static async Task<(string Name, AttributeCheck Check, Dictionary<string, string[]> Errors)> CheckAsync(CiStore store,
        CiTypeInfo type, string? name, JsonElement? attributes, System.Text.Json.Nodes.JsonObject? stored)
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

    // SQL Server's 13535: this transaction began before the row's current version, so another one changed it.
    private const int SystemVersioningTimeConflict = 13535;

    private static object Attribute(AttributeDefinition a) =>
        new { a.Name, a.Label, dataType = a.Kind, a.Required, a.Choices, a.RefType, a.BuiltIn };

    private static CiTypeInfo TypeOf(IReadOnlyList<CiTypeInfo> types, CiStore.CiRow row) => types.First(t => t.Id == row.CiTypeId);

    private static IResult Detail(HttpContext context, CiDetail detail)
    {
        context.Response.Headers.ETag = Quote(detail.ETag);
        return Results.Ok(detail);
    }

    private static IResult Fields(Dictionary<string, string[]> errors) =>
        Results.ValidationProblem(errors, title: "Some values are not valid.", extensions: new Dictionary<string, object?> { ["code"] = "validation" });

    private static string? IfMatch(HttpContext context)
    {
        var value = context.Request.Headers.IfMatch.ToString().Trim();
        return value.Length > 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : null;
    }

    private static string Quote(string etag) => $"\"{etag}\"";

    private static IResult NotFound() => ApiProblems.Problem(StatusCodes.Status404NotFound, "not-found", "No such CI.");

    private static IResult IfMatchRequired() =>
        ApiProblems.Problem(StatusCodes.Status428PreconditionRequired, "if-match-required", "Send the CI's ETag in If-Match.");

    private static IResult Changed() =>
        ApiProblems.Problem(StatusCodes.Status412PreconditionFailed, "changed", "Someone else changed this CI meanwhile. Reload and try again.");

    [GeneratedRegex("^[a-z][A-Za-z0-9]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex AttributeName();
}

using System.Globalization;
using System.Text.RegularExpressions;
using Goldmark.Api.Audit;
using Goldmark.Api.Auth;
using Goldmark.Api.Data;
using Goldmark.Api.Hosting;
using Goldmark.Api.Security;
using Microsoft.Data.SqlClient;

namespace Goldmark.Api.Inventory;

/// <summary>
/// CI types, their attributes and the relationship types (screen 14; US-10, FR-CM-1, FR-IM-1; DEC-044 items 4 and
/// 8, DEC-045). Everyone signed in reads them; only an Admin adds, changes, hides or deletes, and only custom ones:
/// built-in types, attributes and relationship types are locked (409 <c>built-in</c>). Names never change. A
/// change to an attribute or a relationship type needs its ETag in If-Match (428 without it, 412 if someone else
/// changed it meanwhile). Every change is audited in its transaction.
/// </summary>
internal static partial class TypeEndpoints
{
    private const string TypeTarget = AuditTargetTypes.CiType;
    private const string RelationshipTypeTarget = AuditTargetTypes.RelationshipType;

    /// <summary>PRD §9.3's groups, which screen 14 and the search filter list types under.</summary>
    public static readonly IReadOnlyList<string> Categories = ["Location & ownership", "Compute", "Network", "Storage", "Clustering & SQL", "Business"];

    /// <summary>The icons a custom type can have: the generic one and each built-in type's (the web app maps the keys).</summary>
    public static readonly IReadOnlyList<string> Icons =
    [
        "generic", "location", "team", "person", "hypervisor-manager", "hypervisor-cluster", "hypervisor-host", "chassis", "virtual-machine",
        "server", "appliance", "network-device", "datastore", "storage-group", "storage-array", "windows-cluster", "cluster-network-name",
        "sql-instance", "sql-availability-group", "sql-database", "business-service",
    ];

    public static readonly IReadOnlyList<string> Impacts = ["Down", "None"];

    public const int MaxDescriptionLength = 400;

    public static RouteGroupBuilder MapTypeEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/types", ListAsync)
            .RequireAccess(AccessPolicy.SignedIn)
            .WithDisplayName("Types: list");

        api.MapGet("/types/usage", UsageAsync)
            .RequireAccess(AccessPolicy.Admin)
            .WithDisplayName("Types: usage");

        api.MapPost("/types", AddTypeAsync)
            .RequireAccess(AccessPolicy.Admin)
            .WithDisplayName("Types: add");

        api.MapDelete("/types/{type}", DeleteTypeAsync)
            .RequireAccess(AccessPolicy.Admin)
            .WithDisplayName("Types: delete");

        api.MapPost("/types/{type}/hide", (string type, HttpContext context, IServiceProvider services) => SetHiddenAsync(type, context, services, hidden: true))
            .RequireAccess(AccessPolicy.Admin)
            .WithDisplayName("Types: hide");

        api.MapPost("/types/{type}/show", (string type, HttpContext context, IServiceProvider services) => SetHiddenAsync(type, context, services, hidden: false))
            .RequireAccess(AccessPolicy.Admin)
            .WithDisplayName("Types: show");

        api.MapPost("/types/{type}/attributes", AddAttributeAsync)
            .RequireAccess(AccessPolicy.Admin)
            .WithDisplayName("Types: add an attribute");

        api.MapPut("/types/{type}/attributes/{name}", ChangeAttributeAsync)
            .RequireAccess(AccessPolicy.Admin)
            .WithDisplayName("Types: change an attribute");

        api.MapDelete("/types/{type}/attributes/{name}", DeleteAttributeAsync)
            .RequireAccess(AccessPolicy.Admin)
            .WithDisplayName("Types: delete an attribute");

        api.MapPost("/relationship-types", AddRelationshipTypeAsync)
            .RequireAccess(AccessPolicy.Admin)
            .WithDisplayName("Relationship types: add");

        api.MapPut("/relationship-types/{name}", ChangeRelationshipTypeAsync)
            .RequireAccess(AccessPolicy.Admin)
            .WithDisplayName("Relationship types: change");

        api.MapDelete("/relationship-types/{name}", DeleteRelationshipTypeAsync)
            .RequireAccess(AccessPolicy.Admin)
            .WithDisplayName("Relationship types: delete");

        return api;
    }

    internal sealed record TypeRequest(string? Name, string? Category, string? Icon);

    internal sealed record AttributeRequest(string? Name, string? Label, string? DataType, bool? Required, IReadOnlyList<string>? Choices,
        string? RefType);

    internal sealed record AttributeChangeRequest(string? Label, bool? Required, IReadOnlyList<string>? Choices);

    internal sealed record RelationshipTypeRequest(string? Name, string? From, string? To, string? Impact, string? Description);

    internal sealed record RelationshipTypeChangeRequest(string? Impact, string? Description);

    private static async Task<IResult> ListAsync(CiTypeCatalog catalog) =>
        Results.Ok(new
        {
            types = (await catalog.ListAsync()).Select(Type),
            relationshipTypes = (await catalog.RelationshipTypesAsync()).Select(RelationshipType),
        });

    private static async Task<IResult> UsageAsync(CiTypeCatalog catalog)
    {
        var (types, relationshipTypes) = await catalog.UsageAsync(await catalog.ListAsync(), await catalog.RelationshipTypesAsync());
        return Results.Ok(new { types, relationshipTypes });
    }

    private static async Task<IResult> AddTypeAsync(TypeRequest request, CiTypeCatalog catalog, DbSession db, AuditTrail audit)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var name = request.Name?.Trim() ?? "";
        if (!TypeName().IsMatch(name))
        {
            errors["name"] = [name.Length == 0 ? "Required" : "InvalidName"];
        }

        if (request.Category is null || !Categories.Contains(request.Category, StringComparer.Ordinal))
        {
            errors["category"] = [request.Category is null ? "Required" : "InvalidCategory"];
        }

        var icon = request.Icon ?? "generic";
        if (!Icons.Contains(icon, StringComparer.Ordinal))
        {
            errors["icon"] = ["InvalidIcon"];
        }

        if (errors.Count > 0)
        {
            return Fields(errors);
        }

        try
        {
            return await db.InTransactionAsync(async () =>
            {
                if (await catalog.AddTypeAsync(name, request.Category!, icon) is not { } id)
                {
                    return ApiProblems.Field("name", "DuplicateName");
                }

                await audit.WriteAsync(AuditActions.TypeAdded, TypeTarget, id, new { type = name, group = request.Category, icon });
                return Results.Created("/api/v1/types", Type((await catalog.LockForChangeAsync(id))!));
            });
        }
        catch (SqlException e) when (e.Number is 2601 or 2627)
        {
            return ApiProblems.Field("name", "DuplicateName");
        }
    }

    private static async Task<IResult> DeleteTypeAsync(string type, CiTypeCatalog catalog, DbSession db, AuditTrail audit)
    {
        if (CiTypeCatalog.Find(await catalog.ListAsync(), type) is not { } found)
        {
            return TypeNotFound();
        }

        try
        {
            return await CiEndpoints.RetryOnDeadlockAsync(() => db.InTransactionAsync(async () =>
            {
                if (await catalog.LockForChangeAsync(found.Id) is not { } target)
                {
                    return TypeNotFound();
                }

                if (target.BuiltIn)
                {
                    return BuiltIn("Built-in types can't be changed: Goldmark and its collectors rely on them.");
                }

                var cis = await catalog.CisOfTypeAsync(target.Id);
                if (cis > 0)
                {
                    return ApiProblems.Problem(StatusCodes.Status409Conflict, "has-cis",
                        string.Create(CultureInfo.InvariantCulture, $"{target.Name} has {cis:N0} {(cis == 1 ? "CI" : "CIs")} (retired ones included), so it can't be deleted."),
                        new Dictionary<string, object?> { ["cis"] = cis });
                }

                if (await catalog.ReferencesAsync(target.Id) is [var reference, ..])
                {
                    return Referenced(target.Name, reference);
                }

                await catalog.DeleteTypeAsync(target.Id);
                await audit.WriteAsync(AuditActions.TypeDeleted, TypeTarget, target.Id,
                    new { type = target.Name, group = target.Category, attributesDeleted = target.Attributes.Count });
                return Results.NoContent();
            }));
        }
        catch (SqlException e) when (e.Number == ForeignKeyConflict)
        {
            // Something started to use the type meanwhile (the type's lock covers CIs and these definitions).
            return ApiProblems.Problem(StatusCodes.Status409Conflict, "referenced", "Something started to use this type meanwhile. Reload and try again.");
        }
    }

    private static async Task<IResult> SetHiddenAsync(string type, HttpContext context, IServiceProvider services, bool hidden)
    {
        var catalog = services.GetRequiredService<CiTypeCatalog>();
        var db = services.GetRequiredService<DbSession>();
        var audit = services.GetRequiredService<AuditTrail>();
        if (CiTypeCatalog.Find(await catalog.ListAsync(), type) is not { } found)
        {
            return TypeNotFound();
        }

        return await CiEndpoints.RetryOnDeadlockAsync(() => db.InTransactionAsync(async () =>
        {
            if (await catalog.LockForChangeAsync(found.Id) is not { } target)
            {
                return TypeNotFound();
            }

            if (target.BuiltIn)
            {
                return BuiltIn("Built-in types can't be hidden: Goldmark and its collectors rely on them.");
            }

            if (target.Hidden == hidden)
            {
                return hidden
                    ? ApiProblems.Problem(StatusCodes.Status409Conflict, "hidden", "This type is already hidden.")
                    : ApiProblems.Problem(StatusCodes.Status409Conflict, "not-hidden", "This type isn't hidden.");
            }

            await catalog.SetHiddenAsync(target.Id, hidden);
            await audit.WriteAsync(hidden ? AuditActions.TypeHidden : AuditActions.TypeShown, TypeTarget, target.Id,
                new { type = target.Name, cis = await catalog.CisOfTypeAsync(target.Id) });
            return Results.Ok(Type((await catalog.LockForChangeAsync(target.Id))!));
        }));
    }

    private static async Task<IResult> AddAttributeAsync(string type, AttributeRequest request, HttpContext context, CiTypeCatalog catalog,
        DbSession db, AuditTrail audit)
    {
        var types = await catalog.ListAsync();
        if (CiTypeCatalog.Find(types, type) is not { } found)
        {
            return TypeNotFound();
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var name = request.Name?.Trim() ?? "";
        if (!AttributeName().IsMatch(name))
        {
            errors["name"] = [name.Length == 0 ? "Required" : "InvalidName"];
        }

        var label = LabelError(request.Label, errors);
        var dataType = AttributeDataTypes.FromApi(request.DataType);
        if (dataType is null)
        {
            errors["dataType"] = [request.DataType is null ? "Required" : "InvalidDataType"];
        }

        var choices = ChoicesError(dataType, request.Choices, errors);
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
            return await CiEndpoints.RetryOnDeadlockAsync(() => db.InTransactionAsync(async () =>
            {
                if (await catalog.LockForChangeAsync(found.Id) is not { } target)
                {
                    return TypeNotFound();
                }

                // The type it links to can't be deleted until this commits.
                if (refType is not null && refType.Id != target.Id && await catalog.LockForSaveAsync(refType.Id) is null)
                {
                    return ApiProblems.Field("refType", "UnknownType");
                }

                var duplicates = new Dictionary<string, string[]>(StringComparer.Ordinal);
                if (target.Attributes.Any(a => a.Name == name))
                {
                    duplicates["name"] = ["DuplicateName"];
                }

                if (target.Attributes.Any(a => string.Equals(a.Label, label, StringComparison.OrdinalIgnoreCase)))
                {
                    duplicates["label"] = ["DuplicateLabel"];
                }

                if (duplicates.Count > 0)
                {
                    return Fields(duplicates);
                }

                var id = await catalog.AddAttributeAsync(target.Id, name, label, dataType!, required, choices, refType?.Id, context.User.UserId()!.Value);
                await audit.WriteAsync(AuditActions.AttributeAdded, TypeTarget, target.Id,
                    new { type = target.Name, attribute = label, name, dataType = AttributeDataTypes.ToApi(dataType!), required });
                var added = (await catalog.LockForChangeAsync(target.Id))!.Attributes.First(a => a.Id == id);
                context.Response.Headers.ETag = CiEndpoints.Quote(added.ETag);
                return Results.Created("/api/v1/types", Attribute(added));
            }));
        }
        catch (SqlException e) when (e.Number is 2601 or 2627)
        {
            // The same name or label in another case, where the database's collation tells them apart.
            return e.Message.Contains("UQ_AttributeDef_Label", StringComparison.Ordinal)
                ? ApiProblems.Field("label", "DuplicateLabel")
                : ApiProblems.Field("name", "DuplicateName");
        }
    }

    /// <summary>
    /// Changes a custom attribute's label, required flag and choices (DEC-045 item 2); its name, kind and link target
    /// never change. A choice some CI holds (retired ones included) can't be removed. Turning Required on leaves the
    /// CIs as they are: each needs the value at its next save.
    /// </summary>
    private static async Task<IResult> ChangeAttributeAsync(string type, string name, AttributeChangeRequest request, HttpContext context,
        CiTypeCatalog catalog, DbSession db, AuditTrail audit)
    {
        if (CiEndpoints.IfMatch(context) is not { } etag)
        {
            return IfMatchRequired();
        }

        if (CiTypeCatalog.Find(await catalog.ListAsync(), type) is not { } found)
        {
            return TypeNotFound();
        }

        try
        {
            return await RetryOnConflictAsync(() => db.InTransactionAsync(async () =>
            {
                if (await catalog.LockForChangeAsync(found.Id) is not { } target)
                {
                    return TypeNotFound();
                }

                if (target.Attributes.FirstOrDefault(a => a.Name == name) is not { } attribute)
                {
                    return AttributeNotFound();
                }

                if (attribute.BuiltIn)
                {
                    return BuiltIn("Built-in attributes can't be changed: Goldmark and its collectors rely on them.");
                }

                if (etag != attribute.ETag)
                {
                    return Changed("attribute");
                }

                var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
                var label = LabelError(request.Label, errors);
                if (errors.Count == 0 && target.Attributes.Any(a => a.Id != attribute.Id && string.Equals(a.Label, label, StringComparison.OrdinalIgnoreCase)))
                {
                    errors["label"] = ["DuplicateLabel"];
                }

                // PUT replaces all three (as a CI's PUT does), so a missing flag is refused rather than read as "not required".
                if (request.Required is null)
                {
                    errors["required"] = ["Required"];
                }

                var choices = ChoicesError(attribute.DataType, request.Choices, errors);
                if (errors.Count == 0 && choices is not null)
                {
                    var used = await catalog.ChoiceCountsAsync(target.Id, attribute.Name);
                    if ((attribute.Choices ?? []).Except(choices, StringComparer.Ordinal).Any(c => used.GetValueOrDefault(c) > 0))
                    {
                        errors["choices"] = ["ChoiceInUse"];
                    }
                }

                if (errors.Count > 0)
                {
                    return Fields(errors);
                }

                var required = request.Required!.Value;
                var changed = new List<string>();
                if (label != attribute.Label)
                {
                    changed.Add("Label");
                }

                if (required != attribute.Required)
                {
                    changed.Add("Required");
                }

                if (choices is not null && !choices.SequenceEqual(attribute.Choices ?? [], StringComparer.Ordinal))
                {
                    changed.Add("Choices");
                }

                if (changed.Count > 0)
                {
                    await catalog.ChangeAttributeAsync(attribute.Id, label, required, choices, context.User.UserId()!.Value);
                    await audit.WriteAsync(AuditActions.AttributeChanged, TypeTarget, target.Id,
                        new { type = target.Name, attribute = label, name = attribute.Name, changed });
                }

                var now = (await catalog.LockForChangeAsync(target.Id))!.Attributes.First(a => a.Id == attribute.Id);
                context.Response.Headers.ETag = CiEndpoints.Quote(now.ETag);
                return Results.Ok(Attribute(now));
            }));
        }
        catch (SqlException e) when (e.Number is 2601 or 2627)
        {
            return ApiProblems.Field("label", "DuplicateLabel");
        }
    }

    /// <summary>
    /// Deletes a custom attribute (DEC-045 items 2 and 6): its values leave every CI of the type that has one, retired
    /// ones included, in this one change, by the Admin; each CI's history keeps them, and the definition's history
    /// keeps what they meant (item 8). One audit entry with the count, not one per CI.
    /// </summary>
    private static async Task<IResult> DeleteAttributeAsync(string type, string name, HttpContext context, CiTypeCatalog catalog, DbSession db,
        AuditTrail audit)
    {
        if (CiEndpoints.IfMatch(context) is not { } etag)
        {
            return IfMatchRequired();
        }

        if (CiTypeCatalog.Find(await catalog.ListAsync(), type) is not { } found)
        {
            return TypeNotFound();
        }

        return await RetryOnConflictAsync(() => db.InTransactionAsync(async () =>
        {
            if (await catalog.LockForChangeAsync(found.Id) is not { } target)
            {
                return TypeNotFound();
            }

            if (target.Attributes.FirstOrDefault(a => a.Name == name) is not { } attribute)
            {
                return AttributeNotFound();
            }

            if (attribute.BuiltIn)
            {
                return BuiltIn("Built-in attributes can't be changed: Goldmark and its collectors rely on them.");
            }

            if (etag != attribute.ETag)
            {
                return Changed("attribute");
            }

            var removed = await catalog.DeleteAttributeAsync(target.Id, attribute, context.User.UserId()!.Value);
            await audit.WriteAsync(AuditActions.AttributeDeleted, TypeTarget, target.Id,
                new { type = target.Name, attribute = attribute.Label, name = attribute.Name, valuesRemoved = removed });
            return Results.Ok(new { valuesRemoved = removed });
        }));
    }

    private static async Task<IResult> AddRelationshipTypeAsync(RelationshipTypeRequest request, HttpContext context, CiTypeCatalog catalog,
        DbSession db, AuditTrail audit)
    {
        var types = await catalog.ListAsync();
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var name = request.Name?.Trim() ?? "";
        if (!RelationshipTypeName().IsMatch(name))
        {
            errors["name"] = [name.Length == 0 ? "Required" : "InvalidName"];
        }

        var from = request.From is null ? null : CiTypeCatalog.Find(types, request.From);
        if (request.From is not null && from is null)
        {
            errors["from"] = ["UnknownType"];
        }

        var to = request.To is null ? null : CiTypeCatalog.Find(types, request.To);
        if (request.To is not null && to is null)
        {
            errors["to"] = ["UnknownType"];
        }

        var impact = ImpactError(request.Impact, errors);
        var description = DescriptionError(request.Description, errors);
        if (errors.Count > 0)
        {
            return Fields(errors);
        }

        try
        {
            return await CiEndpoints.RetryOnDeadlockAsync(() => db.InTransactionAsync(async () =>
            {
                // The types it names can't be deleted until this commits.
                foreach (var (field, end) in new[] { ("from", from), ("to", to) })
                {
                    if (end is not null && await catalog.LockForSaveAsync(end.Id) is null)
                    {
                        return ApiProblems.Field(field, "UnknownType");
                    }
                }

                var id = await catalog.AddRelationshipTypeAsync(name, from?.Id, to?.Id, impact, description, context.User.UserId()!.Value);
                await audit.WriteAsync(AuditActions.RelationshipTypeAdded, RelationshipTypeTarget, id,
                    new { name, from = from?.Name, to = to?.Name, impact });
                var added = (await catalog.LockRelationshipTypeAsync(name))!;
                context.Response.Headers.ETag = CiEndpoints.Quote(added.ETag);
                return Results.Created("/api/v1/types", RelationshipType(added));
            }));
        }
        catch (SqlException e) when (e.Number is 2601 or 2627)
        {
            return ApiProblems.Field("name", "DuplicateName");
        }
    }

    /// <summary>Changes a custom relationship type's impact and description (DEC-045 item 4); its name and ends never change.</summary>
    private static async Task<IResult> ChangeRelationshipTypeAsync(string name, RelationshipTypeChangeRequest request, HttpContext context,
        CiTypeCatalog catalog, DbSession db, AuditTrail audit)
    {
        if (CiEndpoints.IfMatch(context) is not { } etag)
        {
            return IfMatchRequired();
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var impact = ImpactError(request.Impact, errors);
        var description = DescriptionError(request.Description, errors);
        if (errors.Count > 0)
        {
            return Fields(errors);
        }

        return await RetryOnConflictAsync(() => db.InTransactionAsync(async () =>
        {
            if (await catalog.LockRelationshipTypeAsync(name) is not { } found)
            {
                return RelationshipTypeNotFound();
            }

            if (found.BuiltIn)
            {
                return BuiltIn("Built-in relationship types can't be changed: Goldmark and its collectors rely on them.");
            }

            if (etag != found.ETag)
            {
                return Changed("relationship type");
            }

            var changed = new List<string>();
            if (impact != found.Impact)
            {
                changed.Add("Impact");
            }

            if (description != found.Description)
            {
                changed.Add("Description");
            }

            if (changed.Count > 0)
            {
                await catalog.ChangeRelationshipTypeAsync(found.Id, impact, description, context.User.UserId()!.Value);
                await audit.WriteAsync(AuditActions.RelationshipTypeChanged, RelationshipTypeTarget, found.Id, new { name = found.Name, changed });
            }

            var now = (await catalog.LockRelationshipTypeAsync(found.Name))!;
            context.Response.Headers.ETag = CiEndpoints.Quote(now.ETag);
            return Results.Ok(RelationshipType(now));
        }));
    }

    /// <summary>Deletes a custom relationship type that no relationship uses (DEC-045 item 4).</summary>
    private static async Task<IResult> DeleteRelationshipTypeAsync(string name, HttpContext context, CiTypeCatalog catalog, DbSession db,
        AuditTrail audit)
    {
        if (CiEndpoints.IfMatch(context) is not { } etag)
        {
            return IfMatchRequired();
        }

        try
        {
            return await RetryOnConflictAsync(() => db.InTransactionAsync(async () =>
            {
                if (await catalog.LockRelationshipTypeAsync(name) is not { } found)
                {
                    return RelationshipTypeNotFound();
                }

                if (found.BuiltIn)
                {
                    return BuiltIn("Built-in relationship types can't be changed: Goldmark and its collectors rely on them.");
                }

                if (etag != found.ETag)
                {
                    return Changed("relationship type");
                }

                var relationships = await catalog.RelationshipsOfTypeAsync(found.Id);
                if (relationships > 0)
                {
                    return ApiProblems.Problem(StatusCodes.Status409Conflict, "in-use",
                        string.Create(CultureInfo.InvariantCulture, $"{relationships:N0} {(relationships == 1 ? "relationship uses" : "relationships use")} it, so it can't be deleted."),
                        new Dictionary<string, object?> { ["relationships"] = relationships });
                }

                await catalog.DeleteRelationshipTypeAsync(found.Id);
                await audit.WriteAsync(AuditActions.RelationshipTypeDeleted, RelationshipTypeTarget, found.Id,
                    new { name = found.Name, from = found.From, to = found.To, impact = found.Impact });
                return Results.NoContent();
            }));
        }
        catch (SqlException e) when (e.Number == ForeignKeyConflict)
        {
            return ApiProblems.Problem(StatusCodes.Status409Conflict, "in-use", "A relationship started to use it meanwhile. Reload and try again.");
        }
    }

    /// <summary>The trimmed label; an error under <c>label</c> when it's missing, too long or has control characters.</summary>
    private static string LabelError(string? value, Dictionary<string, string[]> errors)
    {
        var label = value?.Trim() ?? "";
        if (label.Length == 0 || label.Length > 100 || label.Any(char.IsControl))
        {
            errors["label"] = [label.Length == 0 ? "Required" : "InvalidLabel"];
        }

        return label;
    }

    /// <summary>The trimmed choices of a list of choices (1 to 100, each 1 to 100 characters, no two alike), or null for another kind.</summary>
    private static List<string>? ChoicesError(string? dataType, IReadOnlyList<string>? value, Dictionary<string, string[]> errors)
    {
        var choices = value?.Select(c => c?.Trim() ?? "").ToList();
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

        return dataType == AttributeDataTypes.Enum ? choices : null;
    }

    private static string ImpactError(string? impact, Dictionary<string, string[]> errors)
    {
        if (impact is null || !Impacts.Contains(impact, StringComparer.Ordinal))
        {
            errors["impact"] = [impact is null ? "Required" : "InvalidImpact"];
        }

        return impact ?? "";
    }

    /// <summary>The trimmed description, null when empty; line breaks are kept (as <c>\n</c>), other control characters refused.</summary>
    private static string? DescriptionError(string? value, Dictionary<string, string[]> errors)
    {
        var description = value?.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (description is { Length: > MaxDescriptionLength })
        {
            errors["description"] = ["TooLong"];
        }
        else if (description is not null && description.Any(c => char.IsControl(c) && c != '\n'))
        {
            errors["description"] = ["InvalidDescription"];
        }

        return string.IsNullOrEmpty(description) ? null : description;
    }

    private static object Type(CiTypeInfo t) => new
    {
        t.Id,
        t.Name,
        t.Category,
        t.Icon,
        t.BuiltIn,
        t.Hidden,
        attributes = t.Attributes.Select(Attribute),
    };

    private static object Attribute(AttributeDefinition a) =>
        new { a.Name, a.Label, dataType = a.Kind, a.Required, a.Choices, a.RefType, a.BuiltIn, a.ETag, a.ChangedBy, a.ChangedAt };

    private static object RelationshipType(RelationshipTypeInfo r) =>
        new { r.Id, r.Name, r.From, r.To, r.Impact, r.BuiltIn, r.Description, r.ETag, r.ChangedBy, r.ChangedAt };

    private static IResult Referenced(string type, CiTypeCatalog.TypeReference reference) =>
        ApiProblems.Problem(StatusCodes.Status409Conflict, "referenced", reference.RelationshipType is { } relationshipType
            ? $"The relationship type {relationshipType} names {type}, so it can't be deleted."
            : $"{reference.Label} (an attribute of {reference.Type}) links to {type}, so it can't be deleted.");

    // SQL Server's 547: a foreign key refused the statement.
    private const int ForeignKeyConflict = 547;

    // SQL Server's 13535: this transaction began before a CI's current version (a CI retired while the delete waited for
    // the type's lock), so the system-versioned update can't record it.
    private const int SystemVersioningTimeConflict = 13535;

    /// <summary>
    /// A change that lost a deadlock, or whose transaction began before a CI's current version, runs once more; a
    /// second loss answers 412, and the screen reloads.
    /// </summary>
    private static async Task<IResult> RetryOnConflictAsync(Func<Task<IResult>> change)
    {
        try
        {
            return await change();
        }
        catch (SqlException e) when (e.Number is CiEndpoints.Deadlock or SystemVersioningTimeConflict)
        {
            try
            {
                return await change();
            }
            catch (SqlException again) when (again.Number is CiEndpoints.Deadlock or SystemVersioningTimeConflict)
            {
                return ApiProblems.Problem(StatusCodes.Status412PreconditionFailed, "changed", "Something changed meanwhile. Reload and try again.");
            }
        }
    }

    private static IResult Fields(Dictionary<string, string[]> errors) => CiEndpoints.Fields(errors);

    private static IResult BuiltIn(string title) => ApiProblems.Problem(StatusCodes.Status409Conflict, "built-in", title);

    private static IResult TypeNotFound() => ApiProblems.Problem(StatusCodes.Status404NotFound, "not-found", "No such CI type.");

    private static IResult AttributeNotFound() => ApiProblems.Problem(StatusCodes.Status404NotFound, "not-found", "No such attribute.");

    private static IResult RelationshipTypeNotFound() => ApiProblems.Problem(StatusCodes.Status404NotFound, "not-found", "No such relationship type.");

    private static IResult IfMatchRequired() =>
        ApiProblems.Problem(StatusCodes.Status428PreconditionRequired, "if-match-required", "Send the ETag in If-Match.");

    private static IResult Changed(string what) =>
        ApiProblems.Problem(StatusCodes.Status412PreconditionFailed, "changed", $"Someone else changed this {what} meanwhile. Reload and try again.");

    [GeneratedRegex("^[A-Z][A-Za-z0-9]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex TypeName();

    [GeneratedRegex("^[a-z][A-Za-z0-9]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex AttributeName();

    [GeneratedRegex("^[a-z][a-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex RelationshipTypeName();
}

using Goldmark.Api.Audit;
using Goldmark.Api.Auth;
using Goldmark.Api.Data;
using Goldmark.Api.Hosting;
using Goldmark.Api.Security;
using Goldmark.Api.Users;
using Microsoft.Data.SqlClient;

namespace Goldmark.Api.Tokens;

/// <summary>One entry of a token's usage: what it did, and to what, as the audit log records it.</summary>
internal sealed record ApiTokenUse(long Id, DateTime At, string Action, string Label, string Outcome, ApiTokenUseTarget? Target);

/// <summary>What an entry of a token's usage is about: its audit target type (<c>Ci</c>, <c>Relationship</c>, <c>ApiToken</c>…), ID and name.</summary>
internal sealed record ApiTokenUseTarget(string Type, Guid? Id, string? Name);

/// <summary>
/// Screen 18, API tokens (US-06, DEC-048), for Admins with a session only: no token reaches these endpoints, since
/// none declares a scope. A token's secret is in the one response that creates it, never again; every change is
/// audited in its transaction (<c>token.created</c>, <c>token.revoked</c>), and what a token does is audited with the
/// token as actor (<see cref="ApiTokenHandler"/>).
/// </summary>
internal static class ApiTokenEndpoints
{
    /// <summary>How many of a token's latest audit entries its usage shows (screen 18's "View usage").</summary>
    public const int UsageEntries = 20;

    // SQL Server's 2601 and 2627: a duplicate key in a unique index or constraint.
    private const int DuplicateKey = 2601;
    private const int DuplicateConstraintKey = 2627;

    public static RouteGroupBuilder MapApiTokenEndpoints(this RouteGroupBuilder api)
    {
        var tokens = api.MapGroup("/tokens");

        api.MapGet("/tokens", async (ApiTokenStore store) => Results.Ok(new { tokens = await store.ListAsync() }))
            .RequireAccess(AccessPolicy.Admin)
            .WithDisplayName("API tokens: list");

        api.MapPost("/tokens", CreateAsync)
            .RequireAccess(AccessPolicy.Admin)
            .WithDisplayName("API tokens: create");

        tokens.MapPost("/{id:guid}/revoke", RevokeAsync)
            .RequireAccess(AccessPolicy.Admin)
            .WithDisplayName("API tokens: revoke");

        tokens.MapGet("/{id:guid}/usage", UsageAsync)
            .RequireAccess(AccessPolicy.Admin)
            .WithDisplayName("API tokens: usage");

        return tokens;
    }

    /// <summary>A new token: its name, its scopes, and either <c>expiresInDays</c> (30, 90 or 180) or <c>expiresOn</c> (<c>yyyy-MM-dd</c>).</summary>
    internal sealed record CreateRequest(string? Name, IReadOnlyList<string?>? Scopes, int? ExpiresInDays, string? ExpiresOn);

    /// <summary>
    /// Creates a token and answers 201 with it and its secret, the only time the secret is sent. Every field's errors come
    /// together, those that need the database too: the date's range by the database's clock, and the name's uniqueness.
    /// Audited <c>token.created</c> with the name, the scopes, the expiry and the last four characters, never the secret.
    /// A token dies with its creator's account or Admin role (<see cref="ApiTokenRevocations"/>), so one whose creator was
    /// disabled or lost Admin while the request ran is not created (403).
    /// </summary>
    private static async Task<IResult> CreateAsync(CreateRequest request, HttpContext context, ApiTokenStore store, UserDirectory directory, DbSession db,
        AuditTrail audit)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var name = request.Name?.Trim() ?? "";
        if (ApiTokenNames.Error(name) is { } nameError)
        {
            errors["name"] = [nameError];
        }

        var (scopes, scopeError) = TokenScopes.Parse(request.Scopes);
        if (scopeError is not null)
        {
            errors["scopes"] = [scopeError];
        }

        var (expiry, expiryErrors) = TokenExpiry.Parse(request.ExpiresInDays, request.ExpiresOn);
        foreach (var (field, codes) in expiryErrors)
        {
            errors[field] = codes;
        }

        var admin = context.User.UserId()!.Value;
        var id = Guid.CreateVersion7();
        var secret = ApiTokenSecret.Create();
        try
        {
            return await db.InTransactionAsync(async () =>
            {
                var now = await store.NowAsync();
                if (expiry?.RangeError(DateOnly.FromDateTime(now)) is { } rangeError)
                {
                    errors["expiresOn"] = [rangeError];
                }

                if (!errors.ContainsKey("name") && await store.NameTakenAsync(ApiTokenNames.Normalize(name)))
                {
                    errors["name"] = ["Taken"];
                }

                // Under locks held to the commit: a concurrent disable or removal of Admin has committed (403 here), or it
                // waits for this insert and then revokes the new token with the others.
                if (admin == Guid.Empty && !await directory.IsEnabledAdministratorAsync(admin))
                {
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
                }

                if (errors.Count > 0)
                {
                    return ApiProblems.Fields(errors);
                }

                var expiresAt = expiry!.ExpiresAt(now);
                await store.InsertAsync(id, name, secret, scopes, now, admin, expiresAt);
                await audit.WriteAsync(AuditActions.TokenCreated, AuditTargetTypes.ApiToken, id,
                    new { name, scopes, expiresAt, lastFour = ApiTokenSecret.LastFour(secret) });
                var token = (await store.FindAsync(id))!;
                return Results.Created((string?)null, new
                {
                    token.Id,
                    token.Name,
                    token.LastFour,
                    token.Scopes,
                    token.CreatedAt,
                    token.CreatedBy,
                    token.ExpiresAt,
                    token.LastUsedAt,
                    token.RevokedAt,
                    token.RevokedBy,
                    token.Status,
                    secret,
                });
            });
        }
        catch (SqlException e) when (e.Number is DuplicateKey or DuplicateConstraintKey && e.Message.Contains(ApiTokenStore.NameIndex, StringComparison.Ordinal))
        {
            // Another Admin's token took the name between the check and the insert.
            return ApiProblems.Field("name", "Taken");
        }
    }

    /// <summary>Revokes an active token at once (its next request is 401); audited <c>token.revoked</c>. 409 <c>not-active</c> for one revoked or expired.</summary>
    private static async Task<IResult> RevokeAsync(Guid id, HttpContext context, ApiTokenStore store, DbSession db, AuditTrail audit)
    {
        var admin = context.User.UserId()!.Value;
        return await db.InTransactionAsync(async () =>
        {
            if (!await store.RevokeAsync(id, admin))
            {
                return await store.FindAsync(id) is null
                    ? NotFound()
                    : ApiProblems.Problem(StatusCodes.Status409Conflict, "not-active", "Only an active token can be revoked. This one is revoked or expired already.");
            }

            var token = (await store.FindAsync(id))!;
            await audit.WriteAsync(AuditActions.TokenRevoked, AuditTargetTypes.ApiToken, id, new { name = token.Name, lastFour = token.LastFour });
            return Results.NoContent();
        });
    }

    /// <summary>The token and its latest audit entries as actor, newest first (screen 18's "View usage"); the audit log has the rest.</summary>
    private static async Task<IResult> UsageAsync(Guid id, ApiTokenStore store, AuditLog log)
    {
        if (await store.FindAsync(id) is not { } token)
        {
            return NotFound();
        }

        var query = new AuditQuery(null, null, AuditActorTypes.Token, id, null, [], null, null, Oldest: false);
        var (entries, _) = await log.PageAsync(query, UsageEntries, cursor: null);
        return Results.Ok(new
        {
            token,
            entries = entries.Select(e => new ApiTokenUse(e.Id, e.At, e.Action, e.Label, e.Outcome,
                e.Target is { } target ? new ApiTokenUseTarget(target.Type, target.Id, target.Name ?? target.DisplayName ?? target.UserName) : null)),
        });
    }

    private static IResult NotFound() => ApiProblems.Problem(StatusCodes.Status404NotFound, "not-found", "No such API token.");
}

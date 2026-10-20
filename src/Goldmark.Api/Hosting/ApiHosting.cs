using Goldmark.Api.Audit;
using Goldmark.Api.Auth;
using Goldmark.Api.Users;
using Microsoft.AspNetCore.Identity;

namespace Goldmark.Api.Hosting;

/// <summary>
/// The JSON API under <c>/api/v1</c> (US-50: every UI function is available here). Every state-changing
/// request needs the anti-forgery header (<see cref="ApiAntiforgery"/>), and no API response may be
/// stored by a browser or proxy.
/// </summary>
internal static class ApiHosting
{
    public static IApplicationBuilder UseGoldmarkApiNoStore(this IApplicationBuilder app) => app.Use((context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.CacheControl = "no-store";
                return Task.CompletedTask;
            });
        }

        return next(context);
    });

    public static IEndpointRouteBuilder MapGoldmarkApi(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1").AddEndpointFilter(ApiAntiforgery.FilterAsync);
        api.MapAuthEndpoints();
        api.MapSetupEndpoints();
        api.MapLinkEndpoints();
        api.MapEnrolmentEndpoints();
        api.MapAccountEndpoints();
        api.MapUserEndpoints();
        api.MapAuditEndpoints();
        return endpoints;
    }
}

/// <summary>
/// Goldmark stores every time as UTC, and Dapper reads <c>datetime2</c> as an unspecified kind; the API
/// writes them with the <c>Z</c> designator so a client never takes them for local time.
/// </summary>
internal sealed class UtcDateTimeConverter : System.Text.Json.Serialization.JsonConverter<DateTime>
{
    public override DateTime Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options) =>
        reader.GetDateTime().ToUniversalTime();

    public override void Write(System.Text.Json.Utf8JsonWriter writer, DateTime value, System.Text.Json.JsonSerializerOptions options) =>
        writer.WriteStringValue(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}

/// <summary>Problem Details (RFC 9457) with a stable <c>code</c> the SPA maps to its messages.</summary>
internal static class ApiProblems
{
    public static IResult Problem(int status, string code, string title, IDictionary<string, object?>? extra = null)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = code };
        foreach (var (key, value) in extra ?? new Dictionary<string, object?>())
        {
            extensions[key] = value;
        }

        return Results.Problem(statusCode: status, title: title, extensions: extensions);
    }

    /// <summary>Identity's errors as field errors (HTTP 400, code "validation").</summary>
    public static IResult Validation(IdentityResult result) => Validation(result.Errors);

    public static IResult Validation(IEnumerable<IdentityError> errors) =>
        Results.ValidationProblem(
            errors.GroupBy(e => FieldOf(e.Code)).ToDictionary(g => g.Key, g => g.Select(e => e.Code).ToArray()),
            title: "Some values are not valid.",
            extensions: new Dictionary<string, object?> { ["code"] = "validation" });

    /// <summary>One field error (HTTP 400, code "validation").</summary>
    public static IResult Field(string field, string errorCode) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [errorCode] }, title: "Some values are not valid.",
            extensions: new Dictionary<string, object?> { ["code"] = "validation" });

    private static string FieldOf(string identityCode) => identityCode switch
    {
        _ when identityCode.StartsWith("Password", StringComparison.Ordinal) => "password",
        "DuplicateUserName" or "InvalidUserName" or "UserNameTooLong" => "userName",
        "InvalidDisplayName" => "displayName",
        _ => "",
    };
}

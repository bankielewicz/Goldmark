using Microsoft.Extensions.Options;

namespace Goldmark.Api.Tokens;

/// <summary>API settings (configuration section <c>Goldmark:Api</c>).</summary>
public sealed class ApiOptions
{
    public const string SectionName = "Goldmark:Api";

    public const int MinimumTokenRequestsPerMinute = 1;

    public const int MaximumTokenRequestsPerMinute = 100_000;

    /// <summary>
    /// Requests one API token may make per minute, whatever their endpoints (PRD §5.7 "Rate limiting per token";
    /// DEC-048): HTTP 429 <c>rate-limited</c> with <c>Retry-After</c> beyond it.
    /// </summary>
    public int TokenRequestsPerMinute { get; set; } = 600;
}

internal sealed class ApiOptionsValidator : IValidateOptions<ApiOptions>
{
    public ValidateOptionsResult Validate(string? name, ApiOptions options) =>
        options.TokenRequestsPerMinute is >= ApiOptions.MinimumTokenRequestsPerMinute and <= ApiOptions.MaximumTokenRequestsPerMinute
            ? ValidateOptionsResult.Success
            // Names the setting only, as every configuration error does.
            : ValidateOptionsResult.Fail($"{ApiOptions.SectionName}:{nameof(ApiOptions.TokenRequestsPerMinute)} must be between " +
                $"{ApiOptions.MinimumTokenRequestsPerMinute} and {ApiOptions.MaximumTokenRequestsPerMinute}.");
}

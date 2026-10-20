using System.Text.RegularExpressions;
using Goldmark.Api.Audit;
using Goldmark.Api.Auth;
using Goldmark.Api.Hosting;
using Serilog.Events;

namespace Goldmark.Api.Configuration;

/// <summary>
/// Binds every Goldmark setting once before anything else uses it, so a value of the wrong
/// type (for example <c>Goldmark__Https__Port=abc</c>) becomes a configuration error naming
/// the setting (exit 2) instead of an unhandled exception. The binder's own message contains
/// the offending value, which may be a secret, so only the setting path is ever reported.
/// </summary>
internal static partial class ConfigurationPreflight
{
    public static List<string> BindingErrors(IConfiguration configuration)
    {
        var errors = new List<string>();
        Check(errors, DatabaseOptions.SectionName, () => configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>());
        Check(errors, HttpsOptions.SectionName, () => configuration.GetSection(HttpsOptions.SectionName).Get<HttpsOptions>());
        Check(errors, BootstrapOptions.SectionName, () => configuration.GetSection(BootstrapOptions.SectionName).Get<BootstrapOptions>());
        Check(errors, MigrationOptions.SectionName, () => configuration.GetSection(MigrationOptions.SectionName).Get<MigrationOptions>());
        Check(errors, DataProtectionSettings.SectionName, () => configuration.GetSection(DataProtectionSettings.SectionName).Get<DataProtectionSettings>());
        Check(errors, AuthOptions.SectionName, () => configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>());
        Check(errors, AuditOptions.SectionName, () => configuration.GetSection(AuditOptions.SectionName).Get<AuditOptions>());
        Check(errors, LoggingSetup.MinimumLevelKey, () => configuration.GetValue<LogEventLevel>(LoggingSetup.MinimumLevelKey));
        return errors;
    }

    private static void Check(List<string> errors, string section, Func<object?> bind)
    {
        try
        {
            bind();
        }
        catch (InvalidOperationException ex)
        {
            // Use the path from the message only if it is a plain setting path under this
            // section; otherwise fall back to the section name. Never echo the message.
            var match = SettingPath().Match(ex.Message);
            var path = match.Success && match.Groups[1].Value.StartsWith(section, StringComparison.OrdinalIgnoreCase)
                ? match.Groups[1].Value
                : section;
            errors.Add($"{path} has a value that is not valid for its type.");
        }
    }

    [GeneratedRegex(@" at '([A-Za-z0-9_:]+)' to type '[^']+'\.$")]
    private static partial Regex SettingPath();
}

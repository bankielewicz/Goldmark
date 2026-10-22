using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;

namespace Goldmark.Api.Configuration;

/// <summary>
/// The settings file of the Windows service (DEC-043): the environment variable <c>Goldmark__SettingsFile</c>
/// names a JSON file, which the host and the commands load after <c>appsettings.json</c> and before
/// environment variables, so a variable still overrides it. The installer writes it into an owner-only
/// folder (it holds the recovery code when one is set) and sets the variable on the service.
/// </summary>
internal static class SettingsFile
{
    public const string Key = "Goldmark:SettingsFile";
    public const string Variable = "Goldmark__SettingsFile";

    /// <summary>The file's full path from the environment, or null when none is named.</summary>
    public static string? Path() =>
        Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } path ? path : null;

    /// <summary>Adds the file, if one is named, before the plain environment variables. Returns a problem or null.</summary>
    public static string? Add(IConfigurationBuilder builder) => Add(builder, Path());

    internal static string? Add(IConfigurationBuilder builder, string? path)
    {
        if (path is null)
        {
            return null;
        }

        if (!System.IO.Path.IsPathRooted(path) || !File.Exists(path))
        {
            return $"{Key} must name an existing settings file by its full path.";
        }

        var source = new JsonConfigurationSource { Path = path, Optional = false, ReloadOnChange = false };
        source.ResolveFileProvider();
        // The host also reads DOTNET_- and ASPNETCORE_-prefixed variables before appsettings.json; the file
        // goes before the unprefixed ones, which come after it.
        var sources = builder.Sources.ToList();
        var index = sources.FindLastIndex(s => s is EnvironmentVariablesConfigurationSource { Prefix: null or "" });
        if (index < 0)
        {
            builder.Sources.Add(source);
        }
        else
        {
            builder.Sources.Insert(index, source);
        }

        return null;
    }
}

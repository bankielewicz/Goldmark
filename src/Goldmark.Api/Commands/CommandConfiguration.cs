namespace Goldmark.Api.Commands;

/// <summary>
/// Configuration for helper commands: the same sources as the web host (appsettings.json,
/// appsettings.{Environment}.json, the settings file named by <c>Goldmark__SettingsFile</c>,
/// environment variables).
/// </summary>
internal static class CommandConfiguration
{
    public static IConfigurationRoot Build(out string? problem)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? "Production";
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables();
        problem = Configuration.SettingsFile.Add(builder);
        return builder.Build();
    }
}

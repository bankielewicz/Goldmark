namespace Goldmark.Api.Commands;

/// <summary>
/// Configuration for helper commands: the same sources as the web host
/// (appsettings.json, appsettings.{Environment}.json, environment variables).
/// </summary>
internal static class CommandConfiguration
{
    public static IConfigurationRoot Build()
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? "Production";
        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
    }
}

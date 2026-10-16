using Goldmark.Api.Commands;
using Goldmark.Api.Hosting;
using Microsoft.Extensions.Options;

// Container helper commands share the image and configuration but never start the web host.
switch (args.FirstOrDefault())
{
    case HealthCheckCommand.Name:
        return await HealthCheckCommand.RunAsync();
    case BootstrapDatabaseCommand.Name:
        return await BootstrapDatabaseCommand.RunAsync();
}

var builder = WebApplication.CreateBuilder(args);
builder.AddGoldmark();
try
{
    var app = builder.Build();
    app.UseGoldmark();
    await app.RunAsync();
    return 0;
}
catch (OptionsValidationException ex)
{
    // Fail fast with the setting names only (US-01: startup fails if required secrets are missing).
    await Console.Error.WriteLineAsync("Goldmark cannot start because its configuration is invalid:");
    foreach (var failure in ex.Failures)
    {
        await Console.Error.WriteLineAsync($"  - {failure}");
    }

    return 2;
}

/// <summary>Entry point; public so integration tests can host the application in-process.</summary>
public partial class Program;

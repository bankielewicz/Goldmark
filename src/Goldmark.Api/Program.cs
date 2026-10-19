using Goldmark.Api.Commands;
using Goldmark.Api.Configuration;
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

// A value of the wrong type would otherwise surface as an unhandled binder exception.
var bindingErrors = ConfigurationPreflight.BindingErrors(builder.Configuration);
if (bindingErrors.Count > 0)
{
    return await InvalidConfigurationAsync(bindingErrors);
}

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
    return await InvalidConfigurationAsync(ex.Failures);
}

// Fail fast with the setting names only (US-01: startup fails if required secrets are missing).
static async Task<int> InvalidConfigurationAsync(IEnumerable<string> failures)
{
    await Console.Error.WriteLineAsync("Goldmark cannot start because its configuration is invalid:");
    foreach (var failure in failures)
    {
        await Console.Error.WriteLineAsync($"  - {failure}");
    }

    return 2;
}

/// <summary>Entry point; public so integration tests can host the application in-process.</summary>
public partial class Program;

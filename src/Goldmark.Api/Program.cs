using Goldmark.Api.Commands;
using Goldmark.Api.Configuration;
using Goldmark.Api.Hosting;
using Microsoft.Extensions.Options;

// Container helper commands share the image and configuration but never start the web host.
if (ContainerCommands.IsCommand(args.FirstOrDefault()))
{
    var configuration = CommandConfiguration.Build(out var settingsProblem);
    if (settingsProblem is not null)
    {
        await Console.Error.WriteLineAsync($"{args[0]}: invalid configuration:{Environment.NewLine}  - {settingsProblem}");
        return args[0] == HealthCheckCommand.Name ? 1 : 2; // Docker reserves 2 for HEALTHCHECK commands
    }

    return await ContainerCommands.RunAsync(args, configuration, Console.Out, Console.Error);
}

// A Windows service starts in the system folder; its content root is where the program is (DEC-043).
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = WindowsService.IsRunning ? AppContext.BaseDirectory : null,
});

var errors = new List<string>();
if (SettingsFile.Add(builder.Configuration) is { } problem)
{
    errors.Add(problem);
}
else
{
    // A value of the wrong type would otherwise surface as an unhandled binder exception.
    errors.AddRange(ConfigurationPreflight.BindingErrors(builder.Configuration));
    if (errors.Count == 0)
    {
        var account = builder.Configuration.GetSection(ServiceOptions.SectionName).Get<ServiceOptions>()?.Account;
        errors.AddRange(LoggingSetup.Errors(builder.Configuration, builder.Environment.ContentRootPath, account));
        if (SettingsFile.Path() is { } settings
            && Goldmark.Api.Security.PrivateDirectory.Problem($"{SettingsFile.Key}'s folder", Path.GetDirectoryName(settings)!, account) is { } folder)
        {
            errors.Add(folder);
        }
    }
}

if (errors.Count > 0)
{
    return await InvalidConfigurationAsync(errors);
}

builder.AddGoldmark();
try
{
    var app = builder.Build();
    // Validated before the host starts: as a Windows service, a failure after that point would look like a
    // crash to Windows, and restart-on-failure would repeat it without end (measured, DEC-043 item 11).
    app.Services.GetRequiredService<IStartupValidator>().Validate();
    // Taken now: RunAsync disposes the host, and its services with it, when it returns.
    var rights = app.Services.GetRequiredService<Goldmark.Api.Health.RuntimeRights>();
    app.UseGoldmark();
    await app.RunAsync();
    // 3 when the login's rights were refused at start (Goldmark:Database:CheckRightsAtStart).
    return rights.ExitCode;
}
catch (OptionsValidationException ex)
{
    return await InvalidConfigurationAsync(ex.Failures);
}

// Fail fast with the setting names only (US-01: startup fails if required secrets are missing).
static async Task<int> InvalidConfigurationAsync(IEnumerable<string> failures)
{
    var message = $"Goldmark cannot start because its configuration is invalid:{Environment.NewLine}"
        + string.Join(Environment.NewLine, failures.Select(f => $"  - {f}"));
    await Console.Error.WriteLineAsync(message);
    if (OperatingSystem.IsWindows() && WindowsService.IsRunning)
    {
        WindowsService.RefuseStart(message, WindowsService.InvalidConfigurationEvent);
    }

    return 2;
}

/// <summary>Entry point; public so integration tests can host the application in-process.</summary>
public partial class Program;

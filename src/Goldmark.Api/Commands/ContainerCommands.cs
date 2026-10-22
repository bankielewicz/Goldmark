namespace Goldmark.Api.Commands;

/// <summary>
/// Helper commands that share the image and configuration but never start the web host.
/// Database commands can be chained (<c>bootstrap-db migrate</c>, as the <c>db-init</c>
/// container runs them): they run in order and the first non-zero exit code stops the chain.
/// </summary>
internal static class ContainerCommands
{
    private static readonly Dictionary<string, Func<IConfiguration, TextWriter, TextWriter, Task<int>>> Chainable = new(StringComparer.Ordinal)
    {
        [BootstrapDatabaseCommand.Name] = BootstrapDatabaseCommand.RunAsync,
        [MigrateCommand.Name] = MigrateCommand.RunAsync,
    };

    /// <summary>
    /// Any positional first argument is a command; host options start with '-'. A mistyped
    /// command therefore fails (exit 2) instead of starting a web host that never exits.
    /// </summary>
    public static bool IsCommand(string? argument) =>
        !string.IsNullOrEmpty(argument) && !argument.StartsWith('-');

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IConfiguration configuration, TextWriter output, TextWriter error)
    {
        if (args[0] == HealthCheckCommand.Name)
        {
            if (args.Count > 1)
            {
                // 1, not 2: Docker reserves exit code 2 for HEALTHCHECK commands.
                await error.WriteLineAsync($"{HealthCheckCommand.Name}: takes no arguments.");
                return 1;
            }

            return await HealthCheckCommand.RunAsync();
        }

        if (args.FirstOrDefault(a => !Chainable.ContainsKey(a)) is { } unknown)
        {
            await error.WriteLineAsync($"Unknown command '{unknown}'. Commands: {HealthCheckCommand.Name} (alone), or one or more of: {string.Join(", ", Chainable.Keys)}.");
            return 2;
        }

        foreach (var name in args)
        {
            var code = await Chainable[name](configuration, output, error);
            if (code != 0)
            {
                return code;
            }
        }

        return 0;
    }
}

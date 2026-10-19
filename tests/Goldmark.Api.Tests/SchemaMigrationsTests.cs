using Goldmark.Api.Commands;
using Goldmark.Api.Configuration;
using Goldmark.Api.Data;
using Microsoft.Extensions.Configuration;

namespace Goldmark.Api.Tests;

/// <summary>The embedded migrations and the schema version derived from them (DEC-022).</summary>
public sealed class SchemaMigrationsTests
{
    private static readonly string MigrationsDirectory = Path.Combine(RepositoryRoot(), "db", "migrations");

    [Fact]
    public void Every_script_in_db_migrations_is_embedded_and_nothing_else_is()
    {
        var files = Directory.GetFiles(MigrationsDirectory, "*.sql").Select(f => SchemaMigrations.ResourcePrefix + Path.GetFileName(f)).Order(StringComparer.Ordinal);

        Assert.Equal(files, SchemaMigrations.Scripts);
    }

    [Fact]
    public void Script_versions_are_contiguous_from_1_and_the_newest_is_the_expected_version()
    {
        var versions = SchemaMigrations.Scripts.Select(SchemaMigrations.VersionOf).ToList();

        Assert.All(versions, v => Assert.NotNull(v));
        Assert.Equal(Enumerable.Range(1, versions.Count).Cast<int?>(), versions);
        Assert.Equal(versions.Count, SchemaMigrations.ExpectedVersion);
    }

    [Theory]
    [InlineData("Goldmark.Migrations.0001_baseline.sql", 1)]
    [InlineData("Goldmark.Migrations.0042_add_things.sql", 42)]
    [InlineData("Goldmark.Migrations.9999_future.sql", 9999)]
    [InlineData("Goldmark.Migrations.001_short.sql", null)]
    [InlineData("Goldmark.Migrations.0001_Upper.sql", null)]
    [InlineData("Other.0001_baseline.sql", null)]
    [InlineData("0001_baseline.sql", null)]
    public void Version_of_a_script_name(string name, int? expected) =>
        Assert.Equal(expected, SchemaMigrations.VersionOf(name));

    [Fact]
    public void Database_version_is_the_newest_applied_script_or_0()
    {
        Assert.Equal(0, SchemaMigrations.DatabaseVersion([]));
        Assert.Equal(0, SchemaMigrations.DatabaseVersion(["SomethingElse.sql"]));
        Assert.Equal(2, SchemaMigrations.DatabaseVersion(["Goldmark.Migrations.0002_b.sql", "Goldmark.Migrations.0001_a.sql"]));
    }

    [Fact]
    public void The_runtime_role_never_gets_UPDATE_or_DELETE_on_the_audit_log()
    {
        var role = File.ReadAllText(Path.Combine(MigrationsDirectory, "0002_runtime_role.sql"));

        Assert.Contains("DENY UPDATE, DELETE ON OBJECT::cmdb.AuditLog TO goldmark_runtime;", role, StringComparison.Ordinal);
    }

    internal static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Goldmark.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}

/// <summary>Container helper commands and how db-init chains them.</summary>
public sealed class ContainerCommandsTests
{
    private static async Task<(int Code, string Output, string Error)> RunAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await ContainerCommands.RunAsync(args, new ConfigurationBuilder().Build(), output, error);
        return (code, output.ToString(), error.ToString());
    }

    [Theory]
    [InlineData("bootstrap-db", true)]
    [InlineData("migrate", true)]
    [InlineData("healthcheck", true)]
    [InlineData("bootstrap-dbb", true)] // a typo is an unknown command (exit 2), never the web host
    [InlineData("--urls", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Recognises_container_commands(string? argument, bool expected) =>
        Assert.Equal(expected, ContainerCommands.IsCommand(argument));

    [Fact]
    public async Task An_unknown_command_in_a_chain_runs_nothing_and_exits_2()
    {
        var (code, output, error) = await RunAsync("bootstrap-db", "migrat");

        Assert.Equal(2, code);
        Assert.Contains("Unknown command 'migrat'", error, StringComparison.Ordinal);
        Assert.Empty(output);
    }

    [Fact]
    public async Task A_mistyped_single_command_exits_2()
    {
        var (code, _, error) = await RunAsync("migrat");

        Assert.Equal(2, code);
        Assert.Contains("Unknown command 'migrat'", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_chain_stops_at_the_first_failure()
    {
        // No settings at all: bootstrap-db fails validation (exit 2) and migrate never runs.
        var (code, _, error) = await RunAsync("bootstrap-db", "migrate");

        Assert.Equal(2, code);
        Assert.Contains("bootstrap-db: invalid configuration", error, StringComparison.Ordinal);
        Assert.DoesNotContain("migrate:", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Migrate_reports_missing_settings_by_name()
    {
        var (code, _, error) = await RunAsync("migrate");

        Assert.Equal(2, code);
        Assert.Contains("Goldmark:Database:Host is required", error, StringComparison.Ordinal);
        Assert.Contains("Goldmark:Migration:Password is required", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Healthcheck_takes_no_arguments_and_never_exits_2()
    {
        var (code, _, error) = await RunAsync("healthcheck", "migrate");

        Assert.Equal(1, code);
        Assert.Contains("takes no arguments", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Migrate_validation_rejects_reserved_and_identical_logins()
    {
        var db = new DatabaseOptions { Host = "sql", User = "dbo", Password = "x" };
        var migration = new MigrationOptions { User = "dbo", Password = "y" };

        var errors = MigrateCommand.Validate(db, migration, new BootstrapOptions());

        Assert.Contains(errors, e => e.StartsWith("Goldmark:Database:User", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("Goldmark:Migration:User", StringComparison.Ordinal));
    }
}

using Goldmark.Api.Commands;
using Goldmark.Api.Configuration;
using Goldmark.Api.Data;
using Goldmark.Api.Hosting;
using Microsoft.Extensions.Configuration;

namespace Goldmark.Api.Tests;

/// <summary>
/// The Windows service's settings (DEC-043): Windows authentication, migrations as the administrator, the settings
/// file, the log files. The rules hold on every platform (rule 8); only the folders and DPAPI are Windows-only.
/// </summary>
public sealed class WindowsSettingsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("goldmark-settings-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static (DatabaseOptions Db, MigrationOptions Migration, BootstrapOptions Bootstrap, ServiceOptions Service) WindowsLayout() => (
        new DatabaseOptions { Host = "sql01", Authentication = DatabaseOptions.WindowsAuthentication, CheckRightsAtStart = true },
        new MigrationOptions { Login = MigrationOptions.AdministratorLogin },
        new BootstrapOptions { AdminAuthentication = DatabaseOptions.WindowsAuthentication },
        new ServiceOptions { Account = "CORP\\svc-goldmark$" });

    [Fact]
    public void The_Windows_layout_is_valid_for_bootstrap_db_and_migrate()
    {
        var (db, migration, bootstrap, service) = WindowsLayout();

        Assert.Empty(BootstrapDatabaseCommand.Validate(db, migration, bootstrap, service));
        Assert.Empty(MigrateCommand.Validate(db, migration, bootstrap, service));
        Assert.True(new DatabaseOptionsValidator().Validate(null, db).Succeeded);
    }

    [Fact]
    public void Windows_authentication_needs_the_service_account_and_no_runtime_name_or_password()
    {
        var (db, migration, bootstrap, _) = WindowsLayout();
        db.User = "goldmark_app";
        db.Password = "Some-Pa55word-123";

        var errors = BootstrapDatabaseCommand.Validate(db, migration, bootstrap, new ServiceOptions());

        Assert.Contains(errors, e => e.StartsWith("Goldmark:Database:User, Password and CredentialFile must be empty with Authentication 'Windows'", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("Goldmark:Service:Account must name the service's Windows account", StringComparison.Ordinal));
        Assert.All(errors, e => Assert.DoesNotContain("Some-Pa55word-123", e, StringComparison.Ordinal));
        Assert.False(new DatabaseOptionsValidator().Validate(null, db).Succeeded);
    }

    [Fact]
    public void Windows_authentication_has_no_dedicated_migration_login()
    {
        var (db, _, bootstrap, service) = WindowsLayout();

        var errors = BootstrapDatabaseCommand.Validate(db, new MigrationOptions { User = "goldmark_migrator", Password = "Migrator-Pa55word-1" }, bootstrap, service);

        Assert.Contains("Goldmark:Migration:Login must be 'Administrator' with Goldmark:Database:Authentication 'Windows'.", errors);
    }

    [Fact]
    public void A_Windows_administrator_takes_no_name_password_or_file()
    {
        var (db, migration, _, service) = WindowsLayout();
        var bootstrap = new BootstrapOptions { AdminAuthentication = DatabaseOptions.WindowsAuthentication, AdminUser = "sa", AdminPassword = "x" };

        Assert.Contains(BootstrapDatabaseCommand.Validate(db, migration, bootstrap, service),
            e => e.StartsWith("Goldmark:Bootstrap:AdminUser, AdminPassword and AdminCredentialFile must be empty with AdminAuthentication 'Windows'", StringComparison.Ordinal));
    }

    [Fact]
    public void Migrations_as_a_SQL_administrator_need_its_login_and_never_the_generated_one()
    {
        var db = new DatabaseOptions { Host = "sql01", User = "goldmark_app_x", Password = "Runtime-Pa55word-1" };
        var migration = new MigrationOptions { Login = MigrationOptions.AdministratorLogin };

        var errors = MigrateCommand.Validate(db, migration, new BootstrapOptions { AdminCredentialFile = "/var/lib/goldmark/db-admin/admin.json" });

        Assert.Contains(errors, e => e.StartsWith("Goldmark:Bootstrap:AdminUser and AdminPassword are required with Goldmark:Migration:Login 'Administrator'", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("Goldmark:Bootstrap:AdminCredentialFile", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("CORP\\svc-goldmark$", true)]
    [InlineData("CORP\\svc-goldmark", true)]
    [InlineData("NT SERVICE\\Goldmark", true)]
    [InlineData("corp.example\\Goldmark Service", true)]
    [InlineData("svc-goldmark", false)]
    [InlineData("CORP\\svc'; DROP LOGIN sa; --", false)]
    [InlineData("CORP\\a]b", false)]
    [InlineData("CORP\\a\\b", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_service_account_is_a_plain_Windows_name(string? account, bool valid) =>
        Assert.Equal(valid, WindowsAccount.IsValid(account));

    [Fact]
    public void A_Windows_connection_carries_no_name_or_password()
    {
        var builder = SqlConnectionFactory.BuildIntegrated(new DatabaseOptions { Host = "sql01", Name = "Goldmark" });

        Assert.True(builder.IntegratedSecurity);
        Assert.DoesNotContain("User ID", builder.ConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", builder.ConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.False(builder.TrustServerCertificate);
        Assert.Equal(Microsoft.Data.SqlClient.SqlConnectionEncryptOption.Strict, builder.Encrypt);
    }

    [Fact]
    public async Task Under_Windows_authentication_there_is_nothing_to_rotate()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Goldmark:Database:Host"] = "sql01",
            ["Goldmark:Database:Authentication"] = "Windows",
            ["Goldmark:Migration:Login"] = "Administrator",
            ["Goldmark:Bootstrap:AdminAuthentication"] = "Windows",
            ["Goldmark:Service:Account"] = "CORP\\svc-goldmark$",
        }).Build();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var code = await ContainerCommands.RunAsync(["rotate-db-credentials"], configuration, output, error);

        Assert.Equal(0, code);
        Assert.StartsWith("rotate-db-credentials: nothing to rotate", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_settings_file_comes_after_appsettings_and_before_environment_variables()
    {
        var file = Path.Combine(_root, "goldmark.json");
        File.WriteAllText(file, """{"Goldmark": {"Database": {"Host": "from-file"}}}""");
        var builder = new ConfigurationBuilder()
            .AddEnvironmentVariables("DOTNET_") // the host's prefixed variables, read before appsettings.json
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Goldmark:Database:Host"] = "from-appsettings", ["Goldmark:Database:Port"] = "1444" })
            .AddEnvironmentVariables();

        Assert.Null(SettingsFile.Add(builder, file));

        Assert.IsType<Microsoft.Extensions.Configuration.Json.JsonConfigurationSource>(builder.Sources[2]);
        Assert.IsType<Microsoft.Extensions.Configuration.EnvironmentVariables.EnvironmentVariablesConfigurationSource>(builder.Sources[3]);
        var configuration = builder.Build();
        Assert.Equal("from-file", configuration["Goldmark:Database:Host"]);
        Assert.Equal("1444", configuration["Goldmark:Database:Port"]);
    }

    [Fact]
    public void A_missing_or_relative_settings_file_is_named()
    {
        Assert.Equal("Goldmark:SettingsFile must name an existing settings file by its full path.",
            SettingsFile.Add(new ConfigurationBuilder(), Path.Combine(_root, "missing.json")));
        Assert.Equal("Goldmark:SettingsFile must name an existing settings file by its full path.",
            SettingsFile.Add(new ConfigurationBuilder(), "goldmark.json"));
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("1", true)]
    [InlineData("31", true)]
    [InlineData("3651", false)]
    public void The_number_of_log_files_kept_is_checked(string retained, bool valid)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [LoggingSetup.RetainedFilesKey] = retained,
            [LoggingSetup.DirectoryKey] = Path.Combine(_root, "logs"),
        }).Build();

        Assert.Equal(valid, LoggingSetup.Errors(configuration, _root, serviceAccount: null).Count == 0);
        Assert.True(Directory.Exists(Path.Combine(_root, "logs")));
    }
}

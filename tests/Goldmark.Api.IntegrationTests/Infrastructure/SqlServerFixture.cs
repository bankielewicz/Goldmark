using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using Goldmark.Api.Commands;
using Goldmark.Api.Configuration;
using Goldmark.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Testcontainers.MsSql;

namespace Goldmark.Api.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class SqlServerTests : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SQL Server 2025";
}

/// <summary>
/// One SQL Server 2025 container per test collection: the image digest from
/// deploy/docker-compose.yml, started through the same deploy/sqlserver/sql-entrypoint.sh as
/// Compose, so it forces TDS 8 strict encryption and clients pin its generated certificate
/// exactly as the application does. Tests isolate themselves with their own database and
/// login names.
/// </summary>
public sealed partial class SqlServerFixture : IAsyncLifetime
{
    public const string CertificateHostName = "sqlserver";

    private readonly string _work = Directory.CreateTempSubdirectory("goldmark-it-").FullName;
    private readonly ConcurrentDictionary<string, Lazy<Task<Provisioned>>> _shared = new();
    private int _sequence;
    private MsSqlContainer? _container;

    public string SaPassword { get; } = Secret();

    public string Host { get; private set; } = "";

    public int Port { get; private set; }

    public string CertificatePath { get; private set; } = "";

    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>The SQL Server image, pinned by digest, exactly as Compose runs it.</summary>
    public static string Image { get; } = ComposeSqlImage();

    public async ValueTask InitializeAsync()
    {
        // The resource reaper (Ryuk) is a privileged helper image pulled by tag; the fixture
        // disposes its container itself instead (DEC-027).
        TestcontainersSettings.ResourceReaperEnabled = false;
        _container = new MsSqlBuilder(Image)
            .WithPassword(SaPassword)
            .WithEnvironment("MSSQL_PID", "Express")
            .WithEnvironment("GOLDMARK_SQL_TLS_HOSTNAME", CertificateHostName)
            .WithBindMount(Path.Combine(RepositoryRoot, "deploy", "sqlserver"), "/goldmark/scripts", AccessMode.ReadOnly)
            .WithTmpfsMount("/goldmark/sql-tls-private")
            .WithTmpfsMount("/goldmark/sql-tls-public")
            .WithEntrypoint("/bin/bash", "/goldmark/scripts/sql-entrypoint.sh")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("/bin/bash", "/goldmark/scripts/healthcheck.sh"))
            .Build();
        await _container.StartAsync(TestContext.Current.CancellationToken);

        Host = _container.Hostname;
        Port = _container.GetMappedPublicPort(MsSqlBuilder.MsSqlPort);
        // Docker's archive API cannot read from a tmpfs mount, so read the public certificate with cat.
        var certificate = await _container.ExecAsync(["cat", "/goldmark/sql-tls-public/sqlserver.crt"], TestContext.Current.CancellationToken);
        if (certificate.ExitCode != 0 || !certificate.Stdout.Contains("BEGIN CERTIFICATE", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Could not read the SQL Server certificate: {certificate.Stderr}");
        }

        CertificatePath = Path.Combine(_work, "sqlserver.crt");
        await File.WriteAllTextAsync(CertificatePath, certificate.Stdout, TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }

        Directory.Delete(_work, recursive: true);
    }

    /// <summary>Unique names and passwords for one isolated Goldmark installation on this server.</summary>
    public Installation NewInstallation()
    {
        var n = Interlocked.Increment(ref _sequence);
        return new Installation($"GoldmarkIt{n}", $"it{n}_app", Secret(), $"it{n}_migrator", Secret());
    }

    /// <summary>Command settings for an installation, as the db-init container receives them.</summary>
    public IConfiguration Configuration(Installation installation, IDictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Goldmark:Database:Host"] = Host,
            ["Goldmark:Database:Port"] = Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Goldmark:Database:Name"] = installation.Database,
            ["Goldmark:Database:User"] = installation.RuntimeUser,
            ["Goldmark:Database:Password"] = installation.RuntimePassword,
            ["Goldmark:Database:ServerCertificatePath"] = CertificatePath,
            ["Goldmark:Database:HostNameInCertificate"] = CertificateHostName,
            ["Goldmark:Migration:User"] = installation.MigrationUser,
            ["Goldmark:Migration:Password"] = installation.MigrationPassword,
            ["Goldmark:Bootstrap:AdminUser"] = "sa",
            ["Goldmark:Bootstrap:AdminPassword"] = SaPassword,
            ["Goldmark:Bootstrap:WaitSeconds"] = "30",
        };
        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    /// <summary>Runs container commands (e.g. <c>bootstrap-db migrate</c>) in-process, as db-init does.</summary>
    public async Task<CommandResult> RunAsync(Installation installation, string[] commands, IDictionary<string, string?>? overrides = null)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await ContainerCommands.RunAsync(commands, Configuration(installation, overrides), output, error);
        return new CommandResult(code, output.ToString(), error.ToString());
    }

    /// <summary>A fresh installation with bootstrap-db and migrate applied (asserted).</summary>
    public async Task<Provisioned> ProvisionAsync()
    {
        var installation = NewInstallation();
        var result = await RunAsync(installation, ["bootstrap-db", "migrate"]);
        Assert.True(result.Code == 0, result.ToString());
        return new Provisioned(installation, result);
    }

    /// <summary>One provisioned installation shared by all tests that ask for the same key.</summary>
    public Task<Provisioned> SharedAsync(string key) =>
        _shared.GetOrAdd(key, _ => new Lazy<Task<Provisioned>>(ProvisionAsync)).Value;

    public DatabaseOptions DatabaseOptions(Installation installation) => new()
    {
        Host = Host,
        Port = Port,
        Name = installation.Database,
        User = installation.RuntimeUser,
        Password = installation.RuntimePassword,
        ServerCertificatePath = CertificatePath,
        HostNameInCertificate = CertificateHostName,
    };

    public async Task<SqlConnection> OpenAsync(string user, string password, string database)
    {
        var builder = SqlConnectionFactory.Build(new DatabaseOptions
        {
            Host = Host,
            Port = Port,
            ServerCertificatePath = CertificatePath,
            HostNameInCertificate = CertificateHostName,
        }, user, password, database);
        builder.Pooling = false;
        var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }

    public async Task ExecuteAsync(string user, string password, string database, string sql)
    {
        await using var connection = await OpenAsync(user, password, database);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    public async Task<T> ScalarAsync<T>(string user, string password, string database, string sql)
    {
        await using var connection = await OpenAsync(user, password, database);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)Convert.ChangeType(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken), typeof(T), System.Globalization.CultureInfo.InvariantCulture)!;
    }

    public Task AsAdminAsync(string database, string sql) => ExecuteAsync("sa", SaPassword, database, sql);

    public Task<T> AdminScalarAsync<T>(string database, string sql) => ScalarAsync<T>("sa", SaPassword, database, sql);

    /// <summary>Meets SQL Server's password policy and Goldmark's 12-128 character rule.</summary>
    public static string Secret() => $"{Convert.ToHexString(RandomNumberGenerator.GetBytes(12))}-aA9";

    [GeneratedRegex(@"^x-sql-image:\s*&sql-image\s+(\S+@sha256:[0-9a-f]{64})\s*$", RegexOptions.Multiline)]
    private static partial Regex ComposeSqlImagePattern();

    private static string ComposeSqlImage()
    {
        var compose = File.ReadAllText(Path.Combine(RepositoryRoot, "deploy", "docker-compose.yml"));
        var match = ComposeSqlImagePattern().Match(compose);
        return match.Success ? match.Groups[1].Value : throw new InvalidOperationException("x-sql-image (pinned by digest) not found in deploy/docker-compose.yml");
    }

    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Goldmark.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (Goldmark.slnx) not found above the test output directory.");
    }
}

public sealed record Installation(string Database, string RuntimeUser, string RuntimePassword, string MigrationUser, string MigrationPassword);

public sealed record Provisioned(Installation Installation, CommandResult Result);

public sealed record CommandResult(int Code, string Output, string Error)
{
    public override string ToString() => $"exit {Code}\n--- output\n{Output}\n--- error\n{Error}";
}

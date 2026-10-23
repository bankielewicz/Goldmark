using System.Text.RegularExpressions;
using Goldmark.Api.Commands;
using Goldmark.Api.Configuration;
using Goldmark.Api.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Goldmark.Api.Tests;

/// <summary>
/// Credential files (slice 2c, DEC-034): generated names and passwords, the file format, atomic
/// private writes, and how bootstrap-db, migrate and the application read them.
/// </summary>
public sealed partial class CredentialFileTests : IDisposable
{
    private const string FilePassword = "File-Pa55word-NEVER-LOG-2c11";
    private readonly string _root = Directory.CreateTempSubdirectory("goldmark-credentials-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string PathOf(string name) => Path.Combine(_root, name);

    private string WriteFile(string name, string content)
    {
        var path = PathOf(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [GeneratedRegex("^goldmark_app_[a-z0-9]{10}$")]
    private static partial Regex GeneratedRuntimeName();

    [Fact]
    public void Generated_names_have_the_prefix_the_purpose_and_ten_random_characters() // owner decision 2A
    {
        var names = Enumerable.Range(0, 50).Select(_ => CredentialFile.GenerateName("app")).ToList();

        Assert.All(names, n => Assert.Matches(GeneratedRuntimeName(), n));
        Assert.All(names, n => Assert.True(SqlIdentifier.IsValid(n)));
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Generated_passwords_are_32_letters_and_digits_of_every_kind()
    {
        var passwords = Enumerable.Range(0, 200).Select(_ => CredentialFile.GeneratePassword()).ToList();

        Assert.All(passwords, p =>
        {
            Assert.Equal(32, p.Length);
            Assert.True(p.All(char.IsAsciiLetterOrDigit));
            Assert.Contains(p, char.IsAsciiLetterUpper);
            Assert.Contains(p, char.IsAsciiLetterLower);
            Assert.Contains(p, char.IsAsciiDigit);
        });
        Assert.Equal(passwords.Count, passwords.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void A_written_file_reads_back_and_is_private()
    {
        var path = PathOf(Path.Combine("db-runtime", "credential.json"));
        var credential = new LoginCredential("goldmark_app_abcdefghij", FilePassword);

        CredentialFile.Write(path, credential);

        Assert.True(CredentialFile.TryRead(path, out var read, out _));
        Assert.Equal(credential, read);
        Assert.False(File.Exists(path + ".tmp"));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Path.GetDirectoryName(path)!));
        }
    }

    [Fact]
    public void Writing_again_replaces_the_file_and_tightens_a_wider_directory()
    {
        var directory = PathOf("wide");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "credential.json");
        if (OperatingSystem.IsWindows())
        {
            // Windows refuses a folder that inherits its permissions, and changes nothing (DEC-043 item 5).
            var refused = Assert.Throws<IOException>(() => CredentialFile.Write(path, new LoginCredential("goldmark_app_aaaaaaaaaa", FilePassword)));
            Assert.Contains("inherits its permissions", refused.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(path));
            return;
        }

        File.SetUnixFileMode(directory, (UnixFileMode)0b111_101_101); // 0755, as a fresh mount point may be
        CredentialFile.Write(path, new LoginCredential("goldmark_app_aaaaaaaaaa", "Old-Pa55word-123"));
        CredentialFile.Write(path, new LoginCredential("goldmark_app_aaaaaaaaaa", FilePassword));

        Assert.True(CredentialFile.TryRead(path, out var read, out _));
        Assert.Equal(FilePassword, read.Password);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(directory));
        }
    }

    public static TheoryData<string?, string> BadFiles => new()
    {
        { null, "does not point to an existing file" },
        { "", "is not a valid Goldmark credential file" },
        { "not json " + FilePassword, "is not a valid Goldmark credential file" },
        { "[\"goldmark_app\", \"" + FilePassword + "\"]", "is not a valid Goldmark credential file" },
        { "{\"user\": \"goldmark_app\"}", "is not a valid Goldmark credential file" },
        { "{\"user\": \"goldmark_app\", \"password\": \"" + FilePassword + "\", \"extra\": 1}", "is not a valid Goldmark credential file" },
        { "{\"user\": \"goldmark app;--\", \"password\": \"" + FilePassword + "\"}", "is not a valid Goldmark credential file" },
        { "{\"user\": \"goldmark_app\", \"password\": \"short\"}", "is not a valid Goldmark credential file" },
        { "{\"user\": \"goldmark_app\", \"password\": 12345678901234}", "is not a valid Goldmark credential file" },
    };

    [Theory]
    [MemberData(nameof(BadFiles))]
    public void A_bad_file_is_reported_without_its_contents(string? content, string expected)
    {
        var path = content is null ? PathOf("missing.json") : WriteFile("bad.json", content);

        Assert.False(CredentialFile.TryRead(path, out _, out var problem));
        Assert.Equal(expected, problem);
        Assert.DoesNotContain(FilePassword, problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Readers_use_the_file_and_settings_given_as_well_must_match_it()
    {
        var path = WriteFile("runtime.json", $"{{\"user\": \"goldmark_app_x\", \"password\": \"{FilePassword}\"}}");

        Assert.Null(CredentialFile.Resolve("Goldmark:Database", path, "", "", out var fromFile));
        Assert.Equal(new LoginCredential("goldmark_app_x", FilePassword), fromFile);
        Assert.Null(CredentialFile.Resolve("Goldmark:Database", path, "goldmark_app_x", FilePassword, out _));
        Assert.Null(CredentialFile.Resolve("Goldmark:Database", null, "goldmark_app", "Settings-Pa55word", out var fromSettings));
        Assert.Equal(new LoginCredential("goldmark_app", "Settings-Pa55word"), fromSettings);

        var differs = CredentialFile.Resolve("Goldmark:Database", path, "", "Other-Pa55word-123", out _);
        Assert.Equal("Goldmark:Database:User and Password differ from Goldmark:Database:CredentialFile. Set one or the other, or run bootstrap-db, which rewrites the file from them.", differs);
        Assert.Equal("Goldmark:Migration:CredentialFile does not point to an existing file.",
            CredentialFile.Resolve("Goldmark:Migration", PathOf("missing.json"), "", "", out _));
    }

    [Fact]
    public void Migrate_reads_both_logins_from_their_files()
    {
        var runtime = WriteFile("runtime.json", $"{{\"user\": \"goldmark_app_x\", \"password\": \"{FilePassword}\"}}");
        var migrator = WriteFile("migration.json", "{\"user\": \"goldmark_migrator_x\", \"password\": \"Migrator-Pa55word-1\"}");
        var db = new DatabaseOptions { CredentialFile = runtime };
        var migration = new MigrationOptions { CredentialFile = migrator };

        Assert.Empty(MigrateCommand.ApplyCredentialFiles(db, migration, out var runtimeReadable));
        Assert.True(runtimeReadable);
        Assert.Equal(("goldmark_app_x", FilePassword), (db.User, db.Password));
        Assert.Equal(("goldmark_migrator_x", "Migrator-Pa55word-1"), (migration.User, migration.Password));
    }

    [Fact]
    public async Task The_application_reads_its_login_from_the_credential_file()
    {
        await using var factory = new GoldmarkFactory();
        factory.Settings["Goldmark:Database:User"] = null;
        factory.Settings["Goldmark:Database:Password"] = null;
        // Written as bootstrap-db does, in an owner-only folder (encrypted to this account on Windows).
        var path = Path.Combine(Path.GetDirectoryName(factory.CreateFile("unused"))!, "db-runtime", "credential.json");
        CredentialFile.Write(path, new LoginCredential("goldmark_app_x", FilePassword));
        factory.Settings["Goldmark:Database:CredentialFile"] = path;

        var db = factory.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var ready = await factory.CreateClient().GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(("goldmark_app_x", FilePassword), (db.User, db.Password));
        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, ready.StatusCode); // nothing listens on the database port
        Assert.DoesNotContain(FilePassword, factory.Logs.RenderAll(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{\"user\": \"goldmark_app\"}")]
    public void The_application_refuses_a_missing_or_invalid_credential_file(string? content)
    {
        var db = new DatabaseOptions { Host = "sqlserver", CredentialFile = content is null ? PathOf("missing.json") : WriteFile("bad.json", content) };

        DatabaseOptionsValidator.ApplyCredentialFile(db);
        var result = new DatabaseOptionsValidator().Validate(null, db);

        Assert.True(result.Failed);
        Assert.Single(result.Failures!, f => f.StartsWith("Goldmark:Database:CredentialFile ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_host_refuses_to_start_without_its_credential_file()
    {
        await using var factory = new GoldmarkFactory();
        factory.Settings["Goldmark:Database:User"] = null;
        factory.Settings["Goldmark:Database:Password"] = null;
        factory.Settings["Goldmark:Database:CredentialFile"] = PathOf("missing.json");

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    private async Task<(int Code, string Error)> BootstrapAsync(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Goldmark:Database:Host"] = "127.0.0.1",
            ["Goldmark:Database:Port"] = "1",
            ["Goldmark:Database:CredentialFile"] = PathOf(Path.Combine("db-runtime", "credential.json")),
            ["Goldmark:Migration:CredentialFile"] = PathOf(Path.Combine("db-admin", "migration.json")),
            ["Goldmark:Bootstrap:AdminUser"] = "goldmark_dba",
            ["Goldmark:Bootstrap:AdminPassword"] = "Admin-Pa55word-1",
            ["Goldmark:Bootstrap:WaitSeconds"] = "0",
        }.Concat(settings).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Last().Value)).Build();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await BootstrapDatabaseCommand.RunAsync(configuration, output, error);
        return (code, error.ToString());
    }

    [Fact]
    public async Task Bootstrap_refuses_a_login_name_without_its_password()
    {
        var (code, error) = await BootstrapAsync(new() { ["Goldmark:Database:User"] = "goldmark_app" });

        Assert.Equal(2, code);
        Assert.Contains("Goldmark:Database:User is set without Goldmark:Database:Password", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mode_Existing_generates_nothing()
    {
        var (code, error) = await BootstrapAsync(new() { ["Goldmark:Bootstrap:Mode"] = "Existing" });

        Assert.Equal(2, code);
        Assert.Contains("Goldmark:Database:User and Password are required in Mode Existing", error, StringComparison.Ordinal);
        Assert.Contains("Goldmark:Migration:User and Password are required in Mode Existing", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bootstrap_refuses_an_invalid_credential_file_without_echoing_it()
    {
        WriteFile(Path.Combine("db-runtime", "credential.json"), "{\"user\": \"x\", \"password\": \"" + FilePassword + "\", \"x\": 1}");
        var admin = WriteFile(Path.Combine("db-admin", "admin.json"), "garbage " + FilePassword);

        var (code, error) = await BootstrapAsync(new()
        {
            ["Goldmark:Bootstrap:AdminUser"] = "sa",
            ["Goldmark:Bootstrap:AdminCredentialFile"] = admin,
        });

        Assert.Equal(2, code);
        Assert.Contains("Goldmark:Database:CredentialFile is not a valid Goldmark credential file.", error, StringComparison.Ordinal);
        Assert.Contains("Goldmark:Bootstrap:AdminCredentialFile is not a valid Goldmark credential file.", error, StringComparison.Ordinal);
        Assert.DoesNotContain(FilePassword, error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bootstrap_with_nothing_configured_generates_and_then_needs_a_server()
    {
        // Planning succeeds (both logins generated); the run then fails to connect (exit 1), and
        // nothing was written: files are written only after the pre-change audit.
        var (code, _) = await BootstrapAsync([]);

        Assert.Equal(1, code);
        Assert.False(Directory.Exists(PathOf("db-runtime")));
        Assert.False(Directory.Exists(PathOf("db-admin")));
    }
}

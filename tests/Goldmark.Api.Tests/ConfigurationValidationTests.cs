using System.Security.Cryptography.X509Certificates;
using Goldmark.Api.Commands;
using Goldmark.Api.Configuration;
using Goldmark.Api.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Goldmark.Api.Tests;

/// <summary>
/// US-01: no default passwords; startup fails when required secrets are missing. The validators
/// run at startup (ValidateOnStart); the Compose smoke test proves the real container exits with
/// these messages. Here each rule is exercised directly.
/// </summary>
public sealed class ConfigurationValidationTests
{
    private static DatabaseOptions ValidDatabase() => new()
    {
        Host = "sqlserver",
        User = "goldmark_app",
        Password = TestSecrets.DatabasePassword,
    };

    public static TheoryData<Action<DatabaseOptions>, string> UnsafeDatabaseSettings => new()
    {
        { o => o.Password = "", "Goldmark:Database:Password is required" },
        { o => o.User = "", "Goldmark:Database:User is required" },
        { o => o.User = "SA", "least-privilege login, not 'sa'" },
        { o => o.Host = " ", "Goldmark:Database:Host is required" },
        { o => o.Encrypt = "Optional", "must be 'Strict' or 'Mandatory'" },
        { o => o.Encrypt = "False", "must be 'Strict' or 'Mandatory'" },
        { o => o.Name = "Goldmark];DROP", "simple identifier" },
        { o => o.Name = "master", "system database" },
        { o => o.Name = "MODEL", "system database" },
        { o => o.Name = "tempdb", "system database" },
        { o => o.ServerCertificatePath = "/nonexistent/sql.crt", "ServerCertificatePath does not point" },
        { o => o.Port = 0, "Port must be between" },
        { o => o.ConnectTimeoutSeconds = 0, "ConnectTimeoutSeconds must be between" },
    };

    [Fact]
    public void Valid_database_settings_pass() =>
        Assert.True(new DatabaseOptionsValidator().Validate(null, ValidDatabase()).Succeeded);

    [Theory]
    [MemberData(nameof(UnsafeDatabaseSettings))]
    public void Unsafe_or_missing_database_settings_fail(Action<DatabaseOptions> mutate, string expected)
    {
        var options = ValidDatabase();
        mutate(options);

        var result = new DatabaseOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains(expected, StringComparison.Ordinal));
        Assert.DoesNotContain(TestSecrets.DatabasePassword, result.FailureMessage, StringComparison.Ordinal);
    }

    public static TheoryData<Action<HttpsOptions>, string> UnsafeHttpsSettings => new()
    {
        { o => o.CertificatePath = "/etc/goldmark/server.crt", "CertificatePath and KeyPath must be set together" },
        { o => o.KeyPath = "/etc/goldmark/server.key", "CertificatePath and KeyPath must be set together" },
        { o => { o.CertificatePath = "/nonexistent/server.crt"; o.KeyPath = "/nonexistent/server.key"; }, "does not point to a readable file" },
        { o => o.Hostnames = "bad host!,localhost", "invalid DNS name" },
        { o => o.Hostnames = " , ", "at least one name" },
        { o => o.GeneratedCertificateDirectory = "", "GeneratedCertificateDirectory is required" },
        { o => o.Hsts.MaxAgeDays = 0, "MaxAgeDays must be between" },
        { o => o.Port = 70000, "Port must be between" },
    };

    [Fact]
    public void Operator_certificate_files_must_load_as_a_matching_PEM_pair()
    {
        var dir = Directory.CreateTempSubdirectory("goldmark-pem-").FullName;
        try
        {
            using var first = Goldmark.Api.Security.TlsCertificateProvider.Create(["a.example"], DateTimeOffset.UtcNow);
            using var second = Goldmark.Api.Security.TlsCertificateProvider.Create(["b.example"], DateTimeOffset.UtcNow);
            using var secondKey = second.GetECDsaPrivateKey()!;
            var cert = Path.Combine(dir, "server.crt");
            var key = Path.Combine(dir, "server.key");
            var garbage = Path.Combine(dir, "garbage.pem");
            var encrypted = Path.Combine(dir, "encrypted.key");
            File.WriteAllText(cert, first.ExportCertificatePem());
            File.WriteAllText(key, secondKey.ExportPkcs8PrivateKeyPem());
            File.WriteAllText(garbage, "not a certificate");
            File.WriteAllText(encrypted, secondKey.ExportEncryptedPkcs8PrivateKeyPem("pw",
                new System.Security.Cryptography.PbeParameters(System.Security.Cryptography.PbeEncryptionAlgorithm.Aes256Cbc,
                    System.Security.Cryptography.HashAlgorithmName.SHA256, 10_000)));

            foreach (var (c, k) in new[] { (cert, key), (garbage, key), (cert, garbage), (cert, encrypted) })
            {
                var result = new HttpsOptionsValidator().Validate(null, new HttpsOptions { CertificatePath = c, KeyPath = k });
                Assert.True(result.Failed, $"{c} + {k} should fail");
                Assert.Contains(result.Failures!, f => f.Contains("could not be loaded", StringComparison.Ordinal));
            }

            using var firstKey = first.GetECDsaPrivateKey()!;
            File.WriteAllText(key, firstKey.ExportPkcs8PrivateKeyPem());
            Assert.True(new HttpsOptionsValidator().Validate(null, new HttpsOptions { CertificatePath = cert, KeyPath = key }).Succeeded);

            // Valid leaf and key, but a corrupt (valid base64, invalid DER) intermediate: startup imports
            // the whole bundle, so validation must too.
            var corruptChain = Path.Combine(dir, "corrupt-chain.crt");
            File.WriteAllText(corruptChain, first.ExportCertificatePem() + "\n-----BEGIN CERTIFICATE-----\nAAAAAAAA\n-----END CERTIFICATE-----\n");
            var chained = new HttpsOptionsValidator().Validate(null, new HttpsOptions { CertificatePath = corruptChain, KeyPath = key });
            Assert.True(chained.Failed);
            Assert.Contains(chained.Failures!, f => f.Contains("could not be loaded", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void A_pinned_SQL_certificate_must_parse()
    {
        var dir = Directory.CreateTempSubdirectory("goldmark-sqlpin-").FullName;
        try
        {
            var garbage = Path.Combine(dir, "garbage.crt");
            File.WriteAllText(garbage, "-----BEGIN CERTIFICATE-----\nnot base64\n-----END CERTIFICATE-----\n");
            var options = ValidDatabase();
            options.ServerCertificatePath = garbage;
            var result = new DatabaseOptionsValidator().Validate(null, options);
            Assert.True(result.Failed);
            Assert.Contains(result.Failures!, f => f.Contains("could not be read as a PEM or DER certificate", StringComparison.Ordinal));

            using var certificate = Goldmark.Api.Security.TlsCertificateProvider.Create(["sqlserver"], DateTimeOffset.UtcNow);
            var pem = Path.Combine(dir, "sqlserver.crt");
            var der = Path.Combine(dir, "sqlserver.cer");
            File.WriteAllText(pem, certificate.ExportCertificatePem());
            File.WriteAllBytes(der, certificate.RawData);
            foreach (var good in new[] { pem, der })
            {
                options.ServerCertificatePath = good;
                Assert.True(new DatabaseOptionsValidator().Validate(null, options).Succeeded, good);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Default_https_settings_pass() =>
        Assert.True(new HttpsOptionsValidator().Validate(null, new HttpsOptions()).Succeeded);

    [Theory]
    [MemberData(nameof(UnsafeHttpsSettings))]
    public void Invalid_https_settings_fail(Action<HttpsOptions> mutate, string expected)
    {
        var options = new HttpsOptions();
        mutate(options);

        var result = new HttpsOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains(expected, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Goldmark:Database:Password")]
    [InlineData("Goldmark:Database:User")]
    [InlineData("Goldmark:Database:Host")]
    public async Task Host_refuses_to_start_when_a_required_setting_is_missing(string key)
    {
        await using var factory = new GoldmarkFactory();
        factory.Settings[key] = null;

        // Program prints the failures (names only) and exits; the in-process host never serves.
        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public async Task Relative_certificate_paths_resolve_against_the_content_root()
    {
        await using var factory = new GoldmarkFactory();
        factory.Settings["Goldmark:Https:GeneratedCertificateDirectory"] = "state/tls";

        var https = factory.Services.GetRequiredService<IOptions<HttpsOptions>>().Value;
        var contentRoot = factory.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>().ContentRootPath;

        Assert.Equal(Path.GetFullPath("state/tls", contentRoot), https.GeneratedCertificateDirectory);
    }

    [Fact]
    public async Task Validators_are_registered_with_the_host()
    {
        await using var factory = new GoldmarkFactory();

        Assert.Contains(factory.Services.GetServices<IValidateOptions<DatabaseOptions>>(), v => v is DatabaseOptionsValidator);
        Assert.Contains(factory.Services.GetServices<IValidateOptions<HttpsOptions>>(), v => v is HttpsOptionsValidator);
    }
}

public sealed class BootstrapValidationTests
{
    private static DatabaseOptions ValidDatabase() => new()
    {
        Host = "sqlserver",
        Name = "Goldmark",
        User = "goldmark_app",
        Password = "Abcdef123456!",
    };

    private static BootstrapOptions ValidBootstrap() => new() { AdminUser = "sa", AdminPassword = "Admin-Pa55word!" };

    [Fact]
    public void Valid_settings_pass() => Assert.Empty(BootstrapDatabaseCommand.Validate(ValidDatabase(), ValidBootstrap()));

    [Theory]
    [InlineData("sa")]
    [InlineData("goldmark app")]
    [InlineData("x]; DROP LOGIN sa; --")]
    [InlineData("")]
    public void Runtime_login_must_be_a_simple_non_sa_identifier(string user)
    {
        var db = ValidDatabase();
        db.User = user;
        Assert.Contains(BootstrapDatabaseCommand.Validate(db, ValidBootstrap()), e => e.Contains(":User", StringComparison.Ordinal));
    }

    [Fact]
    public void Runtime_login_must_differ_from_the_admin_login()
    {
        var bootstrap = ValidBootstrap();
        bootstrap.AdminUser = "goldmark_app";
        Assert.Contains(BootstrapDatabaseCommand.Validate(ValidDatabase(), bootstrap), e => e.Contains("must differ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(11)]
    [InlineData(129)]
    public void Runtime_password_length_is_bounded(int length)
    {
        var db = ValidDatabase();
        db.Password = new string('a', length);
        Assert.Contains(BootstrapDatabaseCommand.Validate(db, ValidBootstrap()), e => e.Contains(":Password", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(121)]
    public void Connect_timeout_must_be_bounded_so_bootstrap_cannot_hang(int seconds)
    {
        var db = ValidDatabase();
        db.ConnectTimeoutSeconds = seconds;
        Assert.Contains(BootstrapDatabaseCommand.Validate(db, ValidBootstrap()), e => e.Contains("ConnectTimeoutSeconds", StringComparison.Ordinal));
    }

    [Fact]
    public void Least_privilege_verification_checks_effective_permissions_as_the_login()
    {
        // Effective permissions as the login: securityadmin/db_securityadmin passed a fixed-role check.
        var effective = BootstrapDatabaseCommand.VerifySql;
        Assert.Contains("sys.login_token", effective, StringComparison.Ordinal);
        Assert.Contains("sys.user_token", effective, StringComparison.Ordinal);
        Assert.Contains("fn_my_permissions(NULL, N'SERVER')", effective, StringComparison.Ordinal);
        Assert.Contains("fn_my_permissions(NULL, N'DATABASE')", effective, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pinned_SQL_certificate_is_validated_before_connecting()
    {
        var db = ValidDatabase();
        db.ServerCertificatePath = "/nonexistent/sqlserver.crt";
        Assert.Contains(BootstrapDatabaseCommand.Validate(db, ValidBootstrap()), e => e.Contains("ServerCertificatePath does not point", StringComparison.Ordinal));

        var garbage = Path.GetTempFileName();
        try
        {
            File.WriteAllText(garbage, "-----BEGIN CERTIFICATE-----\nnot base64\n-----END CERTIFICATE-----\n");
            db.ServerCertificatePath = garbage;
            Assert.Contains(BootstrapDatabaseCommand.Validate(db, ValidBootstrap()), e => e.Contains("could not be read as a PEM or DER", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(garbage);
        }
    }

    [Fact]
    public void Adopting_an_existing_login_is_off_by_default() =>
        Assert.False(new BootstrapOptions().AdoptExistingLogin);

    [Fact]
    public void Admin_password_is_required()
    {
        var bootstrap = ValidBootstrap();
        bootstrap.AdminPassword = "";
        Assert.Contains(BootstrapDatabaseCommand.Validate(ValidDatabase(), bootstrap), e => e.Contains("AdminPassword", StringComparison.Ordinal));
    }
}

/// <summary>
/// A value of the wrong type must become a clean configuration error (exit 2) that names the
/// setting. The binder's own message contains the value, so it must never be echoed.
/// </summary>
public sealed class ConfigurationPreflightTests
{
    private static List<string> Errors(string key, string value) =>
        ConfigurationPreflight.BindingErrors(new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>(key, value)])
            .Build());

    [Fact]
    public void Well_typed_settings_pass()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Goldmark:Https:Port"] = "8443",
            ["Goldmark:Https:Hsts:Enabled"] = "true",
            ["Goldmark:Database:ConnectTimeoutSeconds"] = "5",
            ["Goldmark:Bootstrap:AdoptExistingLogin"] = "false",
            ["Goldmark:Logging:MinimumLevel"] = "Warning",
        }).Build();

        Assert.Empty(ConfigurationPreflight.BindingErrors(configuration));
    }

    [Theory]
    [InlineData("Goldmark:Https:Port")]
    [InlineData("Goldmark:Https:Hsts:Enabled")]
    [InlineData("Goldmark:Database:ConnectTimeoutSeconds")]
    [InlineData("Goldmark:Bootstrap:AdoptExistingLogin")]
    [InlineData("Goldmark:Logging:MinimumLevel")]
    public void A_wrongly_typed_value_names_the_setting_but_never_the_value(string key)
    {
        const string value = "NEVER-ECHO-5d1c";

        var error = Assert.Single(Errors(key, value));

        Assert.StartsWith(key, error, StringComparison.Ordinal);
        Assert.DoesNotContain(value, error, StringComparison.Ordinal);
    }
}

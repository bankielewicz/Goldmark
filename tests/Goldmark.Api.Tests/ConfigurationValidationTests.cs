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

    private static BootstrapOptions ValidBootstrap() => new() { AdminUser = "goldmark_dba", AdminPassword = "Admin-Pa55word!" };

    private static MigrationOptions ValidMigration() => new() { User = "goldmark_migrator", Password = "Migrate-Pa55word!" };

    private static List<string> Validate(DatabaseOptions db, BootstrapOptions bootstrap, MigrationOptions? migration = null) =>
        BootstrapDatabaseCommand.Validate(db, migration ?? ValidMigration(), bootstrap);

    [Fact]
    public void Valid_settings_pass() => Assert.Empty(Validate(ValidDatabase(), ValidBootstrap()));

    [Fact]
    public void The_migration_login_has_no_default_name_or_password() // DEC-034: generated when neither is set
    {
        var defaults = new MigrationOptions();
        Assert.Equal("", defaults.User);
        Assert.Equal("", defaults.Password);
        Assert.Null(defaults.CredentialFile);
        Assert.Contains(Validate(ValidDatabase(), ValidBootstrap(), defaults), e => e.Contains("Goldmark:Migration:Password must be 12", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Port_must_be_a_TCP_port(int port) // #7
    {
        var db = ValidDatabase();
        db.Port = port;
        Assert.Contains(Validate(db, ValidBootstrap()), e => e.Contains("Goldmark:Database:Port must be between 1 and 65535", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("dbo")]
    [InlineData("guest")]
    [InlineData("sys")]
    [InlineData("INFORMATION_SCHEMA")]
    [InlineData("information_schema")]
    [InlineData("public")]
    [InlineData("goldmark_runtime")]
    [InlineData("SA")]
    public void Reserved_database_principal_names_are_rejected_for_both_logins(string name) // #7
    {
        var db = ValidDatabase();
        db.User = name;
        Assert.Contains(Validate(db, ValidBootstrap()), e => e.Contains("Goldmark:Database:User must not be 'sa' or a reserved", StringComparison.Ordinal));

        var migration = ValidMigration();
        migration.User = name;
        Assert.Contains(Validate(ValidDatabase(), ValidBootstrap(), migration), e => e.Contains("Goldmark:Migration:User must not be 'sa' or a reserved", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("goldmark_app", "must differ from Goldmark:Database:User")]
    [InlineData("GOLDMARK_APP", "must differ from Goldmark:Database:User")]
    [InlineData("sa", "reserved")]
    [InlineData("migrator; DROP", "simple identifier")]
    public void The_migration_login_must_be_its_own_simple_login(string user, string expected)
    {
        var migration = ValidMigration();
        migration.User = user;
        Assert.Contains(Validate(ValidDatabase(), ValidBootstrap(), migration), e => e.StartsWith("Goldmark:Migration:User", StringComparison.Ordinal) && e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void The_migration_login_must_differ_from_the_admin_login()
    {
        var bootstrap = ValidBootstrap();
        bootstrap.AdminUser = "goldmark_migrator";
        Assert.Contains(Validate(ValidDatabase(), bootstrap), e => e.Contains("Goldmark:Migration:User must differ from Goldmark:Bootstrap:AdminUser", StringComparison.Ordinal));
    }

    [Fact]
    public void The_migration_password_must_differ_from_the_runtime_and_admin_passwords()
    {
        var migration = ValidMigration();
        migration.Password = ValidDatabase().Password;
        Assert.Contains(Validate(ValidDatabase(), ValidBootstrap(), migration), e => e.Contains("Goldmark:Migration:Password must differ from Goldmark:Database:Password", StringComparison.Ordinal));

        migration.Password = ValidBootstrap().AdminPassword;
        Assert.Contains(Validate(ValidDatabase(), ValidBootstrap(), migration), e => e.Contains("Goldmark:Migration:Password must differ from Goldmark:Bootstrap:AdminPassword", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(11)]
    [InlineData(129)]
    public void Migration_password_length_is_bounded(int length)
    {
        var migration = ValidMigration();
        migration.Password = new string('m', length);
        Assert.Contains(Validate(ValidDatabase(), ValidBootstrap(), migration), e => e.Contains("Goldmark:Migration:Password must be 12 to 128", StringComparison.Ordinal));
    }

    [Fact]
    public void Validation_messages_never_contain_a_password()
    {
        var db = ValidDatabase();
        var migration = ValidMigration();
        migration.Password = db.Password;
        var bootstrap = ValidBootstrap();
        bootstrap.AdminPassword = db.Password;

        var all = string.Join("\n", Validate(db, bootstrap, migration));

        Assert.DoesNotContain(db.Password, all, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sa")]
    [InlineData("dbo")]
    [InlineData("goldmark app")]
    [InlineData("x]; DROP LOGIN sa; --")]
    [InlineData("")]
    public void Runtime_login_must_be_a_simple_non_sa_identifier(string user)
    {
        var db = ValidDatabase();
        db.User = user;
        Assert.Contains(Validate(db, ValidBootstrap()), e => e.Contains(":User", StringComparison.Ordinal));
    }

    [Fact]
    public void Runtime_login_must_differ_from_the_admin_login()
    {
        var bootstrap = ValidBootstrap();
        bootstrap.AdminUser = "goldmark_app";
        Assert.Contains(Validate(ValidDatabase(), bootstrap), e => e.Contains("must differ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(11)]
    [InlineData(129)]
    public void Runtime_password_length_is_bounded(int length)
    {
        var db = ValidDatabase();
        db.Password = new string('a', length);
        Assert.Contains(Validate(db, ValidBootstrap()), e => e.Contains(":Password", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(121)]
    public void Connect_timeout_must_be_bounded_so_bootstrap_cannot_hang(int seconds)
    {
        var db = ValidDatabase();
        db.ConnectTimeoutSeconds = seconds;
        Assert.Contains(Validate(db, ValidBootstrap()), e => e.Contains("ConnectTimeoutSeconds", StringComparison.Ordinal));
    }

    [Fact]
    public void Least_privilege_verification_checks_effective_permissions_as_the_login()
    {
        // Effective permissions as the login: securityadmin/db_securityadmin passed a fixed-role check.
        foreach (var effective in new[] { BootstrapDatabaseCommand.VerifySql, MigrateCommand.VerifyRuntimeSql })
        {
            Assert.Contains("sys.login_token", effective, StringComparison.Ordinal);
            Assert.Contains("sys.user_token", effective, StringComparison.Ordinal);
            Assert.Contains("fn_my_permissions(NULL, N'SERVER')", effective, StringComparison.Ordinal);
            Assert.Contains("fn_my_permissions(NULL, N'DATABASE')", effective, StringComparison.Ordinal);
        }

        Assert.Contains("sys.login_token", BootstrapDatabaseCommand.VerifyMigrationSql, StringComparison.Ordinal);
        Assert.Contains("fn_my_permissions(NULL, N'SERVER')", BootstrapDatabaseCommand.VerifyMigrationSql, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pinned_SQL_certificate_is_validated_before_connecting()
    {
        var db = ValidDatabase();
        db.ServerCertificatePath = "/nonexistent/sqlserver.crt";
        Assert.Contains(Validate(db, ValidBootstrap()), e => e.Contains("ServerCertificatePath does not point", StringComparison.Ordinal));

        var garbage = Path.GetTempFileName();
        try
        {
            File.WriteAllText(garbage, "-----BEGIN CERTIFICATE-----\nnot base64\n-----END CERTIFICATE-----\n");
            db.ServerCertificatePath = garbage;
            Assert.Contains(Validate(db, ValidBootstrap()), e => e.Contains("could not be read as a PEM or DER", StringComparison.Ordinal));
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
        Assert.Contains(Validate(ValidDatabase(), bootstrap), e => e.Contains("AdminPassword", StringComparison.Ordinal));
    }

    [Fact]
    public void The_admin_login_has_no_default() // DEC-032
    {
        Assert.Equal("", new BootstrapOptions().AdminUser);
        var bootstrap = ValidBootstrap();
        bootstrap.AdminUser = "";
        Assert.Contains(Validate(ValidDatabase(), bootstrap), e => e.Contains("Goldmark:Bootstrap:AdminUser and AdminPassword are required", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("sa")]
    [InlineData("SA")]
    public void Sa_is_refused_as_the_admin_login_unless_it_creates_the_generated_administrator(string admin) // DEC-032, DEC-034
    {
        var bootstrap = ValidBootstrap();
        bootstrap.AdminUser = admin;
        Assert.Contains(Validate(ValidDatabase(), bootstrap), e => e.Contains("Goldmark:Bootstrap:AdminUser must not be 'sa'", StringComparison.Ordinal));

        bootstrap.AdminCredentialFile = "/var/lib/goldmark/db-admin/admin.json";
        Assert.Empty(Validate(ValidDatabase(), bootstrap));
    }

    [Fact]
    public void A_generated_administrator_is_created_by_sa_only() // DEC-034
    {
        var bootstrap = ValidBootstrap();
        bootstrap.AdminCredentialFile = "/var/lib/goldmark/db-admin/admin.json";
        Assert.Contains(Validate(ValidDatabase(), bootstrap), e => e.Contains("Goldmark:Bootstrap:AdminUser must be 'sa' with Goldmark:Bootstrap:AdminCredentialFile", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Create", true)]
    [InlineData("Existing", true)]
    [InlineData("existing", false)]
    [InlineData("", false)]
    [InlineData("Adopt", false)]
    public void Mode_is_Create_or_Existing(string mode, bool valid) // DEC-032
    {
        Assert.Equal(BootstrapOptions.CreateMode, new BootstrapOptions().Mode);
        var bootstrap = ValidBootstrap();
        bootstrap.Mode = mode;
        var errors = Validate(ValidDatabase(), bootstrap);
        Assert.Equal(valid, !errors.Exists(e => e.Contains("Goldmark:Bootstrap:Mode must be 'Create' or 'Existing'", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("guest-connect:Sales", null)]
    [InlineData("guest-connect:Sales, linked-login-all-logins:LS1\nsystem-grant:msdb:public:EXECUTE:OBJECT:[dbo].[sp_x]", null)]
    [InlineData("cross-db-chaining", null)]
    [InlineData("server-role:goldmark_app:sysadmin", "cannot be accepted")]
    [InlineData("login-policy-off:goldmark_app", "cannot be accepted")]
    [InlineData("goldmark-grant:Goldmark:public:SELECT:dbo.X", "cannot be accepted")]
    [InlineData("Guest-Connect:Sales", "is not a finding ID")]
    [InlineData("guest-connect::Sales", "is not a finding ID")]
    [InlineData("guest-connect:Sales;x", "is not a finding ID")]
    public void Accepted_findings_must_be_well_formed_and_about_the_shared_instance(string accepted, string? error) // DEC-031
    {
        var bootstrap = ValidBootstrap();
        bootstrap.AcceptedFindings = accepted.Replace("\\n", "\n", StringComparison.Ordinal);
        var errors = Validate(ValidDatabase(), bootstrap).Where(e => e.StartsWith("Goldmark:Bootstrap:AcceptedFindings", StringComparison.Ordinal)).ToList();
        if (error is null)
        {
            Assert.Empty(errors);
        }
        else
        {
            Assert.Contains(errors, e => e.Contains(error, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void A_malformed_accepted_finding_is_not_echoed() // it may be a secret pasted into the wrong setting
    {
        var bootstrap = ValidBootstrap();
        bootstrap.AcceptedFindings = "guest-connect:Sales Pa55;word!";
        var errors = Validate(ValidDatabase(), bootstrap);
        Assert.Contains(errors, e => e.Contains("entry 2 of 2 is not a finding ID", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, e => e.Contains("Pa55", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unknown_finding_kind_is_not_echoed() // Codex on #17: a lowercase secret matches the ID grammar
    {
        var bootstrap = ValidBootstrap();
        bootstrap.AcceptedFindings = "guest-connect:Sales lowercasesecret42";
        var errors = Validate(ValidDatabase(), bootstrap);
        Assert.Contains(errors, e => e.Contains("entry 2 of 2 is not a known finding kind", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, e => e.Contains("lowercasesecret42", StringComparison.Ordinal));
    }

    [Fact]
    public void Migrate_validates_accepted_findings_too()
    {
        var bootstrap = ValidBootstrap();
        bootstrap.AcceptedFindings = "database-role:goldmark_app:db_owner";
        Assert.Contains(MigrateCommand.Validate(ValidDatabase(), ValidMigration(), bootstrap), e => e.Contains("cannot be accepted", StringComparison.Ordinal));
    }
}

/// <summary>Finding IDs (DEC-031): stable, without separators or white space, and only instance findings are acceptable.</summary>
public sealed class AuditFindingTests
{
    [Theory]
    [InlineData("guest-connect", new[] { "Sales" }, "guest-connect:Sales")]
    [InlineData("public-server-grant", new[] { "VIEW SERVER STATE", "SERVER" }, "public-server-grant:VIEW+SERVER+STATE:SERVER")]
    [InlineData("guest-connect", new[] { "a b" }, "guest-connect:a+b")]
    [InlineData("linked-login-all-logins", new[] { "host,1433" }, "linked-login-all-logins:host%2C1433")]
    [InlineData("system-grant", new[] { "msdb", "public", "EXECUTE", "OBJECT", "[dbo].[sp:x]" }, "system-grant:msdb:public:EXECUTE:OBJECT:[dbo].[sp%3Ax]")]
    [InlineData("system-grant", new[] { "msdb", "public", "SELECT", "SCHEMA", "[sys.all_objects]" }, "system-grant:msdb:public:SELECT:SCHEMA:[sys.all_objects]")]
    [InlineData("database-grant", new[] { "app", "SELECT", "OBJECT", "[dbo].[T] ([a+b%])" }, "database-grant:app:SELECT:OBJECT:[dbo].[T]+%28[a%2Bb%25]%29")]
    [InlineData("server-grant", new[] { "app", "CONTROL SERVER", "SERVER", "" }, "server-grant:app:CONTROL+SERVER:SERVER")]
    [InlineData("guest-connect", new[] { "Ventes-été" }, "guest-connect:Ventes-%C3%A9t%C3%A9")]
    [InlineData("cross-db-chaining", new string[0], "cross-db-chaining")]
    public void Ids_are_encoded_without_separators(string kind, string[] parts, string id)
    {
        var finding = new AuditFinding(kind, parts);
        Assert.Equal(id, finding.Id);
        Assert.Empty(FindingPolicy.Errors(finding.Acceptable ? id : ""));
    }

    [Theory]
    [InlineData("guest-connect", true)]
    [InlineData("linked-login-all-logins", true)]
    [InlineData("remote-login-all-logins", true)]
    [InlineData("public-server-grant", false)]
    [InlineData("system-grant", true)]
    [InlineData("cross-db-chaining", true)]
    [InlineData("database-unreadable", true)]
    [InlineData("goldmark-grant", false)]
    [InlineData("goldmark-database-option", false)]
    [InlineData("goldmark-public-owner", false)]
    [InlineData("login-disabled", false)]
    [InlineData("server-role", false)]
    [InlineData("database-access", false)]
    [InlineData("mapped-into", false)]
    [InlineData("owns", false)]
    public void Only_findings_about_the_shared_instance_are_acceptable(string kind, bool acceptable) =>
        Assert.Equal(acceptable, PrincipalAudit.IsAcceptableKind(kind));

    [Fact]
    public void Every_kind_has_its_own_description()
    {
        string[] kinds =
        [
            "guest-connect", "database-unreadable", "linked-login-all-logins", "remote-login-all-logins", "public-server-grant",
            "system-grant", "cross-db-chaining", "goldmark-grant", "goldmark-database-option", "goldmark-database-owner",
            "goldmark-database-principal", "goldmark-public-owner", "runtime-role-grant", "login-disabled", "login-policy-off",
            "login-expiration-on", "login-expired", "login-locked", "login-must-change", "login-connect-denied", "login-credential",
            "server-role", "server-grant", "owns", "linked-login", "remote-login", "mapped-into", "database-access", "database-role",
            "database-grant", "database-connect-denied", "server-securable", "database-securable",
        ];
        Assert.True(PrincipalAudit.KnownKinds.SetEquals(kinds));
        foreach (var kind in kinds)
        {
            var description = new AuditFinding(kind, ["a", "b", "c", "d"]).Description;
            Assert.False(description.StartsWith($"{kind}:", StringComparison.Ordinal), kind);
        }
    }

    [Theory]
    [InlineData("schema", "cmdb", "Fix: USE [Goldmark]; ALTER AUTHORIZATION ON SCHEMA::[cmdb] TO dbo;")]
    [InlineData("database principal", "goldmark_runtime", "Fix: USE [Goldmark]; ALTER AUTHORIZATION ON ROLE::[goldmark_runtime] TO dbo;")]
    [InlineData("schema", "x]; DROP USER y; --", "ALTER AUTHORIZATION ON SCHEMA::[x]]; DROP USER y; --] TO dbo;")]
    public void A_securable_owned_by_public_names_a_quoted_fix(string kind, string name, string expected) // #18
    {
        var finding = new AuditFinding("goldmark-public-owner", ["Goldmark", kind, name]);
        Assert.Contains(expected, finding.Description, StringComparison.Ordinal);
        Assert.False(finding.Acceptable);
    }

    [Fact]
    public void The_measured_baseline_is_embedded()
    {
        var baseline = PrincipalAudit.Baseline.Value;
        Assert.Contains(PrincipalAudit.BaselineKey("guest", "CONNECT", "DATABASE", ""), baseline["tempdb"]);
        Assert.Contains(PrincipalAudit.BaselineKey("public", "SELECT", "OBJECT", "[sys].[objects]"), baseline["*"]);
        Assert.DoesNotContain(PrincipalAudit.BaselineKey("public", "SELECT", "SCHEMA", "[sys].[objects]"), baseline["*"]);
        Assert.All(PrincipalAudit.SystemDatabases.Where(d => d != "model"), d => Assert.True(baseline.ContainsKey(d), d));
    }
}

/// <summary>Accepting findings by ID (DEC-031).</summary>
public sealed class FindingPolicyTests
{
    private static readonly AuditFinding Guest = new("guest-connect", ["Sales"]);
    private static readonly AuditFinding Role = new("server-role", ["goldmark_app", "sysadmin"]);

    [Fact]
    public void Accepted_instance_findings_are_reported_but_do_not_refuse()
    {
        var policy = FindingPolicy.Parse("guest-connect:Sales");
        using var output = new StringWriter();

        var refused = policy.Refused([Guest, Guest], output, "bootstrap-db");

        Assert.Empty(refused);
        Assert.Equal(1, output.ToString().Split("accepted finding [guest-connect:Sales]").Length - 1);
    }

    [Fact]
    public void Accepted_IDs_match_exactly_because_names_may_differ_only_by_case() // Codex on #17
    {
        var policy = FindingPolicy.Parse("guest-connect:Sales");
        using var output = new StringWriter();
        var lower = new AuditFinding("guest-connect", ["sales"]);

        var refused = policy.Refused([Guest, lower], output, "bootstrap-db");

        Assert.Same(lower, Assert.Single(refused));
    }

    [Fact]
    public void Suggested_fixes_quote_names_so_a_pasted_fix_cannot_run_anything_else() // Codex on #17
    {
        var guest = new AuditFinding("guest-connect", ["x]; DROP DATABASE Goldmark;--"]).Description;
        Assert.Contains("USE [x]]; DROP DATABASE Goldmark;--]; REVOKE CONNECT FROM guest;", guest, StringComparison.Ordinal);

        var linked = new AuditFinding("linked-login-all-logins", ["a'; DROP DATABASE Goldmark;--"]).Description;
        Assert.Contains("@rmtsrvname = N'a''; DROP DATABASE Goldmark;--', @locallogin = NULL;", linked, StringComparison.Ordinal);

        var grant = new AuditFinding("system-grant", ["msdb", "public", "SELECT WITH GRANT OPTION", "OBJECT", "[dbo].[sysjobs]"]).Description;
        Assert.Contains("Fix: USE [msdb]; REVOKE SELECT ON OBJECT::[dbo].[sysjobs] FROM [public] CASCADE;", grant, StringComparison.Ordinal);
    }

    [Fact]
    public void Findings_about_Goldmarks_principals_refuse_even_if_listed()
    {
        var policy = FindingPolicy.Parse("server-role:goldmark_app:sysadmin");
        using var output = new StringWriter();

        Assert.Same(Role, Assert.Single(policy.Refused([Role], output, "bootstrap-db")));
    }

    [Fact]
    public void An_accepted_ID_that_no_longer_occurs_is_a_warning()
    {
        var policy = FindingPolicy.Parse("guest-connect:Sales guest-connect:Old");
        using var output = new StringWriter();
        policy.Refused([Guest], output, "bootstrap-db");

        policy.ReportUnmatched(output, "bootstrap-db");

        Assert.Contains("warning: accepted finding [guest-connect:Old] did not occur", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("[guest-connect:Sales] did not occur", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_refusal_names_every_finding_with_its_fix_and_how_to_accept_instance_findings()
    {
        var text = FindingPolicy.Describe([Guest, Role]);

        Assert.Contains("found 2 problem(s)", text, StringComparison.Ordinal);
        Assert.Contains("[guest-connect:Sales] guest can connect to database 'Sales'", text, StringComparison.Ordinal);
        Assert.Contains("REVOKE CONNECT FROM guest", text, StringComparison.Ordinal);
        Assert.Contains("add guest-connect:Sales to GOLDMARK_DB_ACCEPTED_FINDINGS", text, StringComparison.Ordinal);
        Assert.Contains("[server-role:goldmark_app:sysadmin]", text, StringComparison.Ordinal);
        Assert.DoesNotContain("add server-role", text, StringComparison.Ordinal);
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

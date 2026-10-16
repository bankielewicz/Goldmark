using Goldmark.Api.Commands;
using Goldmark.Api.Configuration;
using Goldmark.Api.Tests.Infrastructure;
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
        { o => o.Hostnames = "bad host!,localhost", "invalid DNS name" },
        { o => o.Hostnames = " , ", "at least one name" },
        { o => o.GeneratedCertificateDirectory = "", "GeneratedCertificateDirectory is required" },
        { o => o.Hsts.MaxAgeDays = 0, "MaxAgeDays must be between" },
        { o => o.Port = 70000, "Port must be between" },
    };

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

    [Fact]
    public void Admin_password_is_required()
    {
        var bootstrap = ValidBootstrap();
        bootstrap.AdminPassword = "";
        Assert.Contains(BootstrapDatabaseCommand.Validate(ValidDatabase(), bootstrap), e => e.Contains("AdminPassword", StringComparison.Ordinal));
    }
}

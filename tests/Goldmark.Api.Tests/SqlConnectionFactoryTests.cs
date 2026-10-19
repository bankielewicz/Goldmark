using Goldmark.Api.Configuration;
using Goldmark.Api.Data;
using Microsoft.Data.SqlClient;

namespace Goldmark.Api.Tests;

/// <summary>NFR-SEC-4: SQL connections are always encrypted and the server certificate is always validated.</summary>
public sealed class SqlConnectionFactoryTests
{
    private static DatabaseOptions Options() => new()
    {
        Host = "sqlserver",
        Port = 1433,
        Name = "Goldmark",
        User = "goldmark_app",
        Password = "Abcdef123456!",
    };

    [Fact]
    public void Defaults_to_TDS8_strict_encryption_without_trusting_the_server_certificate()
    {
        var builder = new SqlConnectionFactory(Microsoft.Extensions.Options.Options.Create(Options())).CreateBuilder();

        Assert.Equal(SqlConnectionEncryptOption.Strict, builder.Encrypt);
        Assert.False(builder.TrustServerCertificate);
        Assert.False(builder.PersistSecurityInfo);
        Assert.Equal("tcp:sqlserver,1433", builder.DataSource);
        Assert.Equal("Goldmark", builder.InitialCatalog);
        Assert.Equal("goldmark_app", builder.UserID);
    }

    [Fact]
    public void Mandatory_encryption_and_certificate_pinning_are_supported()
    {
        var options = Options();
        options.Encrypt = "Mandatory";
        options.ServerCertificatePath = "/run/goldmark/sql-tls/sqlserver.crt";
        options.HostNameInCertificate = "sqlserver";

        var builder = SqlConnectionFactory.Build(options);

        Assert.Equal(SqlConnectionEncryptOption.Mandatory, builder.Encrypt);
        Assert.Equal("/run/goldmark/sql-tls/sqlserver.crt", builder.ServerCertificate);
        Assert.Equal("sqlserver", builder.HostNameInCertificate);
        Assert.False(builder.TrustServerCertificate);
    }

    [Fact]
    public void Connection_string_keywords_cannot_be_injected_through_settings()
    {
        var options = Options();
        options.Password = "x;TrustServerCertificate=true;Encrypt=false";
        options.Host = "sqlserver;TrustServerCertificate=true";

        var builder = new SqlConnectionStringBuilder(SqlConnectionFactory.Build(options).ConnectionString);

        Assert.False(builder.TrustServerCertificate);
        Assert.Equal(SqlConnectionEncryptOption.Strict, builder.Encrypt);
        Assert.Equal("x;TrustServerCertificate=true;Encrypt=false", builder.Password);
    }
}

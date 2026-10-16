using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Goldmark.Api.Configuration;
using Goldmark.Api.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace Goldmark.Api.Tests;

/// <summary>US-01: HTTPS by default with a per-installation certificate generated on first start.</summary>
public sealed class TlsCertificateProviderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("goldmark-tls-").FullName;
    private readonly MutableTimeProvider _time = new(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private HttpsOptions Options(string hostnames = "localhost,goldmark.corp.example,10.0.0.5") => new()
    {
        GeneratedCertificateDirectory = Path.Combine(_dir, "tls"),
        Hostnames = hostnames,
    };

    private ServerCertificate Get(HttpsOptions options) =>
        new TlsCertificateProvider(options, _time, NullLogger.Instance).GetCertificate();

    [Fact]
    public void First_start_generates_and_persists_a_self_signed_server_certificate()
    {
        var options = Options();

        using var cert = Get(options).Certificate;

        Assert.True(cert.HasPrivateKey);
        Assert.NotNull(cert.GetECDsaPublicKey());
        Assert.Equal(cert.Subject, cert.Issuer);
        Assert.True(File.Exists(Path.Combine(options.GeneratedCertificateDirectory, TlsCertificateProvider.CertificateFileName)));
        Assert.True(File.Exists(Path.Combine(options.GeneratedCertificateDirectory, TlsCertificateProvider.KeyFileName)));
        Assert.Equal(
            new HashSet<string> { "localhost", "goldmark.corp.example", "10.0.0.5" },
            TlsCertificateProvider.SubjectNames(cert));
        var eku = Assert.Single(cert.Extensions.OfType<X509EnhancedKeyUsageExtension>());
        Assert.Contains(eku.EnhancedKeyUsages.Cast<Oid>(), o => o.Value == "1.3.6.1.5.5.7.3.1");
        var constraints = Assert.Single(cert.Extensions.OfType<X509BasicConstraintsExtension>());
        Assert.False(constraints.CertificateAuthority);
        Assert.True(cert.NotAfter.ToUniversalTime() - _time.GetUtcNow().UtcDateTime <= TimeSpan.FromDays(398));
    }

    [Fact]
    public void Private_key_file_is_readable_by_the_owner_only()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("POSIX permissions only");
            return;
        }

        var options = Options();

        using var cert = Get(options).Certificate;

        var key = Path.Combine(options.GeneratedCertificateDirectory, TlsCertificateProvider.KeyFileName);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(key));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(options.GeneratedCertificateDirectory));
        Assert.DoesNotContain(".tmp", string.Join(",", Directory.GetFiles(options.GeneratedCertificateDirectory)), StringComparison.Ordinal);
    }

    [Fact]
    public void Restarts_reuse_the_persisted_certificate()
    {
        var options = Options();
        using var first = Get(options).Certificate;

        _time.Advance(TimeSpan.FromDays(100));
        using var second = Get(options).Certificate;

        Assert.Equal(first.Thumbprint, second.Thumbprint);
    }

    [Fact]
    public void Certificate_is_regenerated_within_30_days_of_expiry()
    {
        var options = Options();
        using var first = Get(options).Certificate;

        _time.Advance(TimeSpan.FromDays(368));
        using var second = Get(options).Certificate;

        Assert.NotEqual(first.Thumbprint, second.Thumbprint);
    }

    [Fact]
    public void Certificate_is_regenerated_when_host_names_change()
    {
        using var first = Get(Options("localhost")).Certificate;
        using var second = Get(Options("localhost,cmdb.corp.example")).Certificate;

        Assert.NotEqual(first.Thumbprint, second.Thumbprint);
        Assert.Contains("cmdb.corp.example", TlsCertificateProvider.SubjectNames(second));
    }

    [Fact]
    public void Corrupt_persisted_files_are_replaced()
    {
        var options = Options();
        using (Get(options).Certificate)
        {
        }

        File.WriteAllText(Path.Combine(options.GeneratedCertificateDirectory, TlsCertificateProvider.KeyFileName), "garbage");
        using var replaced = Get(options).Certificate;

        Assert.True(replaced.HasPrivateKey);
    }

    [Fact]
    public void Operator_supplied_certificate_and_chain_are_used_and_nothing_is_generated()
    {
        using var caKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var caRequest = new CertificateRequest("CN=Test Issuing CA", caKey, HashAlgorithmName.SHA256);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var ca = caRequest.CreateSelfSigned(_time.GetUtcNow().AddDays(-1), _time.GetUtcNow().AddYears(2));
        using var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var leafRequest = new CertificateRequest("CN=cmdb.corp.example", leafKey, HashAlgorithmName.SHA256);
        using var leafPublic = leafRequest.Create(ca, _time.GetUtcNow().AddDays(-1), _time.GetUtcNow().AddYears(1), [1, 2, 3, 4]);
        var certPath = Path.Combine(_dir, "server.crt");
        var keyPath = Path.Combine(_dir, "server.key");
        File.WriteAllText(certPath, leafPublic.ExportCertificatePem() + "\n" + ca.ExportCertificatePem());
        File.WriteAllText(keyPath, leafKey.ExportPkcs8PrivateKeyPem());
        var options = Options();
        options.CertificatePath = certPath;
        options.KeyPath = keyPath;

        var served = Get(options);

        Assert.False(served.Generated);
        Assert.Equal(leafPublic.Thumbprint, served.Certificate.Thumbprint);
        Assert.True(served.Certificate.HasPrivateKey);
        Assert.Equal(ca.Thumbprint, Assert.Single(served.Chain).Thumbprint);
        Assert.False(Directory.Exists(options.GeneratedCertificateDirectory));
        Assert.Equal(certPath, TlsCertificateProvider.PublicCertificatePath(options));
    }

    [Theory]
    [InlineData(null, false, false)]
    [InlineData(null, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    public void Hsts_defaults_to_on_only_with_an_operator_certificate(bool? enabled, bool operatorCert, bool expected)
    {
        var options = Options();
        options.Hsts.Enabled = enabled;
        if (operatorCert)
        {
            options.CertificatePath = "/etc/goldmark/server.crt";
            options.KeyPath = "/etc/goldmark/server.key";
        }

        Assert.Equal(expected, HstsPolicy.IsEnabled(options));
    }

    private sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}

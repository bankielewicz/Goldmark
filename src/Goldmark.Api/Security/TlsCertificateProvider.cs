using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Goldmark.Api.Configuration;

namespace Goldmark.Api.Security;

/// <summary>A server certificate plus any intermediate certificates to send with it.</summary>
internal sealed record ServerCertificate(X509Certificate2 Certificate, X509Certificate2Collection Chain, bool Generated);

/// <summary>
/// Provides the HTTPS server certificate (US-01). Uses the operator-supplied PEM certificate
/// when configured; otherwise generates a per-installation self-signed ECDSA P-256 certificate
/// on first start and persists it under <see cref="HttpsOptions.GeneratedCertificateDirectory"/>
/// (a volume outside the image) with an owner-only private key file. The generated certificate
/// is re-created when it is within 30 days of expiry, unreadable, or its host names changed.
/// </summary>
internal sealed partial class TlsCertificateProvider(HttpsOptions options, TimeProvider time, ILogger logger)
{
    public const string CertificateFileName = "goldmark.crt";
    public const string KeyFileName = "goldmark.key";
    internal static readonly TimeSpan Validity = TimeSpan.FromDays(397);
    internal static readonly TimeSpan RenewBefore = TimeSpan.FromDays(30);

    private const UnixFileMode DirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode KeyMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode CertificateMode = KeyMode | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    public static string GeneratedCertificatePath(HttpsOptions options) =>
        Path.Combine(options.GeneratedCertificateDirectory, CertificateFileName);

    /// <summary>The certificate file a client on this host can pin (public part only).</summary>
    public static string PublicCertificatePath(HttpsOptions options) =>
        options.UsesOperatorCertificate ? options.CertificatePath! : GeneratedCertificatePath(options);

    public ServerCertificate GetCertificate()
    {
        if (options.UsesOperatorCertificate)
        {
            var supplied = LoadPem(options.CertificatePath!, options.KeyPath!);
            var chain = LoadChain(options.CertificatePath!, supplied);
            var fingerprint = Fingerprint(supplied);
            var notAfter = supplied.NotAfter.ToUniversalTime();
            LogOperatorCertificate(logger, fingerprint, notAfter);
            if (notAfter <= time.GetUtcNow().UtcDateTime)
            {
                LogOperatorCertificateExpired(logger, fingerprint);
            }

            return new ServerCertificate(supplied, chain, Generated: false);
        }

        var directory = options.GeneratedCertificateDirectory;
        var certPath = Path.Combine(directory, CertificateFileName);
        var keyPath = Path.Combine(directory, KeyFileName);
        EnsurePrivateDirectory(directory);

        if (File.Exists(certPath) && File.Exists(keyPath))
        {
            var reason = TryLoadExisting(certPath, keyPath, out var existing);
            if (existing is not null)
            {
                var existingFingerprint = Fingerprint(existing);
                var existingNotAfter = existing.NotAfter.ToUniversalTime();
                LogUsingGenerated(logger, existingFingerprint, existingNotAfter);
                return new ServerCertificate(existing, [], Generated: true);
            }

            LogRegenerating(logger, reason);
        }

        using (var created = Create(options.HostnameList, time.GetUtcNow()))
        {
            Persist(created, directory, certPath, keyPath);
        }

        var persisted = LoadPem(certPath, keyPath);
        var generatedFingerprint = Fingerprint(persisted);
        var generatedNotAfter = persisted.NotAfter.ToUniversalTime();
        LogGenerated(logger, generatedFingerprint, generatedNotAfter);
        LogGeneratedHostnames(logger, options.Hostnames);
        return new ServerCertificate(persisted, [], Generated: true);
    }

    private string TryLoadExisting(string certPath, string keyPath, out X509Certificate2? certificate)
    {
        certificate = null;
        X509Certificate2 loaded;
        try
        {
            loaded = LoadPem(certPath, keyPath);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return "the persisted certificate or key could not be loaded";
        }

        if (loaded.NotAfter.ToUniversalTime() - time.GetUtcNow().UtcDateTime <= RenewBefore)
        {
            loaded.Dispose();
            return "the persisted certificate expires within 30 days";
        }

        if (!SubjectNames(loaded).SetEquals(options.HostnameList.Select(Normalize)))
        {
            loaded.Dispose();
            return "the configured host names changed";
        }

        certificate = loaded;
        return "";
    }

    internal static X509Certificate2 Create(IReadOnlyList<string> hostnames, DateTimeOffset now)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=Goldmark self-signed certificate", key, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder();
        foreach (var name in hostnames)
        {
            if (IPAddress.TryParse(name, out var ip))
            {
                san.AddIpAddress(ip);
            }
            else
            {
                san.AddDnsName(name);
            }
        }

        request.CertificateExtensions.Add(san.Build(critical: false));
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1", "Server Authentication")], critical: false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        return request.CreateSelfSigned(now.AddMinutes(-5), now.Add(Validity));
    }

    /// <summary>
    /// Creates the directory if needed and, on Unix, forces it to 0700 on every start: a
    /// pre-existing directory (for example a volume mount point) may have been created with
    /// a wider mode by the image build or the container runtime.
    /// </summary>
    private void EnsurePrivateDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
            return;
        }

        Directory.CreateDirectory(directory, DirectoryMode);
        if (File.GetUnixFileMode(directory) == DirectoryMode)
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(directory, DirectoryMode);
        }
        catch (UnauthorizedAccessException)
        {
            LogDirectoryNotPrivate(logger);
        }
    }

    private static void Persist(X509Certificate2 certificate, string directory, string certPath, string keyPath)
    {
        using var key = certificate.GetECDsaPrivateKey()
            ?? throw new InvalidOperationException("Generated certificate has no private key.");
        // Key first: if the process dies between the two writes, the next start sees a
        // certificate/key mismatch and regenerates both.
        WriteAtomically(keyPath, key.ExportPkcs8PrivateKeyPem(), KeyMode);
        WriteAtomically(certPath, certificate.ExportCertificatePem(), CertificateMode);
    }

    private static void WriteAtomically(string path, string contents, UnixFileMode mode)
    {
        var temp = path + ".tmp";
        File.Delete(temp);
        var fileOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            fileOptions.UnixCreateMode = mode;
        }

        using (var stream = new FileStream(temp, fileOptions))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(contents);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }

        File.Move(temp, path, overwrite: true);
    }

    internal static X509Certificate2 LoadPem(string certPath, string keyPath)
    {
        var certificate = X509Certificate2.CreateFromPemFile(certPath, keyPath);
        if (!OperatingSystem.IsWindows())
        {
            return certificate;
        }

        // SChannel cannot use ephemeral keys; round-trip through PKCS#12 on Windows dev machines.
        using (certificate)
        {
            return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pkcs12), password: null);
        }
    }

    private static X509Certificate2Collection LoadChain(string certPath, X509Certificate2 leaf)
    {
        var all = new X509Certificate2Collection();
        all.ImportFromPemFile(certPath);
        var chain = new X509Certificate2Collection();
        foreach (var cert in all)
        {
            if (cert.Thumbprint != leaf.Thumbprint)
            {
                chain.Add(cert);
            }
        }

        return chain;
    }

    internal static HashSet<string> SubjectNames(X509Certificate2 certificate)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ext in certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>())
        {
            names.UnionWith(ext.EnumerateDnsNames().Select(Normalize));
            names.UnionWith(ext.EnumerateIPAddresses().Select(ip => ip.ToString()));
        }

        return names;
    }

    private static string Normalize(string name) =>
        IPAddress.TryParse(name, out var ip) ? ip.ToString() : name.ToLowerInvariant();

    public static string Fingerprint(X509Certificate2 certificate) =>
        Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256));

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information,
        Message = "Using operator-supplied TLS certificate (SHA-256 {Fingerprint}, expires {NotAfter:O})")]
    private static partial void LogOperatorCertificate(ILogger logger, string fingerprint, DateTime notAfter);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Error,
        Message = "The operator-supplied TLS certificate (SHA-256 {Fingerprint}) has expired; replace it")]
    private static partial void LogOperatorCertificateExpired(ILogger logger, string fingerprint);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Information,
        Message = "Using the persisted self-signed TLS certificate (SHA-256 {Fingerprint}, expires {NotAfter:O})")]
    private static partial void LogUsingGenerated(ILogger logger, string fingerprint, DateTime notAfter);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Warning,
        Message = "Regenerating the self-signed TLS certificate because {Reason}; clients that trusted the old certificate must trust the new one")]
    private static partial void LogRegenerating(ILogger logger, string reason);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Warning,
        Message = "Generated a self-signed TLS certificate (SHA-256 {Fingerprint}, expires {NotAfter:O}). Browsers will warn until it is trusted or replaced; see docs/ops/tls-certificates.md")]
    private static partial void LogGenerated(ILogger logger, string fingerprint, DateTime notAfter);

    [LoggerMessage(EventId = 2007, Level = LogLevel.Warning,
        Message = "The TLS certificate directory is not owner-only (0700) and its mode cannot be changed by this process; restrict it")]
    private static partial void LogDirectoryNotPrivate(ILogger logger);

    [LoggerMessage(EventId = 2006, Level = LogLevel.Debug, Message = "Self-signed TLS certificate host names: {Hostnames}")]
    private static partial void LogGeneratedHostnames(ILogger logger, string hostnames);
}

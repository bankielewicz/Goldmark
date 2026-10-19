using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;

namespace Goldmark.Api.Configuration;

/// <summary>
/// Runtime database connection settings (configuration section <c>Goldmark:Database</c>).
/// The connection string is always built from these discrete values, so operators
/// cannot weaken TLS through extra connection-string keywords.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Goldmark:Database";

    public string Host { get; set; } = "";
    public int Port { get; set; } = 1433;
    public string Name { get; set; } = "Goldmark";

    /// <summary>Runtime login. Must be a least-privilege login, never <c>sa</c>.</summary>
    public string User { get; set; } = "";
    public string Password { get; set; } = "";

    /// <summary><c>Strict</c> (TDS 8, default) or <c>Mandatory</c>. Unencrypted connections are not supported.</summary>
    public string Encrypt { get; set; } = "Strict";

    /// <summary>Optional path to the SQL Server certificate to pin (PEM or DER).</summary>
    public string? ServerCertificatePath { get; set; }

    /// <summary>Optional host name to validate in the server certificate when it differs from <see cref="Host"/>.</summary>
    public string? HostNameInCertificate { get; set; }

    public int ConnectTimeoutSeconds { get; set; } = 5;
}

internal sealed class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
{
    public ValidateOptionsResult Validate(string? name, DatabaseOptions options)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Host))
        {
            failures.Add($"{DatabaseOptions.SectionName}:Host is required.");
        }

        if (options.Port is < 1 or > 65535)
        {
            failures.Add($"{DatabaseOptions.SectionName}:Port must be between 1 and 65535.");
        }

        if (!SqlIdentifier.IsValid(options.Name))
        {
            failures.Add($"{DatabaseOptions.SectionName}:Name must be a simple identifier (letters, digits, underscore).");
        }
        else if (SqlIdentifier.IsSystemDatabase(options.Name))
        {
            failures.Add($"{DatabaseOptions.SectionName}:Name must not be a SQL Server system database.");
        }

        if (string.IsNullOrWhiteSpace(options.User))
        {
            failures.Add($"{DatabaseOptions.SectionName}:User is required.");
        }
        else if (string.Equals(options.User, "sa", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"{DatabaseOptions.SectionName}:User must be a least-privilege login, not 'sa'.");
        }

        if (string.IsNullOrEmpty(options.Password))
        {
            failures.Add($"{DatabaseOptions.SectionName}:Password is required (there is no default password).");
        }

        if (options.Encrypt is not ("Strict" or "Mandatory"))
        {
            failures.Add($"{DatabaseOptions.SectionName}:Encrypt must be 'Strict' or 'Mandatory'.");
        }

        failures.AddRange(CertificatePinErrors(options.ServerCertificatePath));

        if (options.ConnectTimeoutSeconds is < 1 or > 120)
        {
            failures.Add($"{DatabaseOptions.SectionName}:ConnectTimeoutSeconds must be between 1 and 120.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// A pinned certificate that cannot be parsed would make every connection fail later;
    /// report it at startup instead. Shared with <c>bootstrap-db</c>.
    /// </summary>
    public static IEnumerable<string> CertificatePinErrors(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            yield break;
        }

        if (!File.Exists(path))
        {
            yield return $"{DatabaseOptions.SectionName}:ServerCertificatePath does not point to a readable file.";
        }
        else if (!CanLoadCertificate(path))
        {
            yield return $"{DatabaseOptions.SectionName}:ServerCertificatePath could not be read as a PEM or DER certificate.";
        }
    }

    private static bool CanLoadCertificate(string path)
    {
        try
        {
            using var certificate = X509CertificateLoader.LoadCertificateFromFile(path);
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Goldmark.Api.Configuration;

/// <summary>HTTPS listener, certificate and HSTS settings (configuration section <c>Goldmark:Https</c>).</summary>
public sealed class HttpsOptions
{
    public const string SectionName = "Goldmark:Https";

    public int Port { get; set; } = 8443;

    /// <summary>Operator-supplied certificate (PEM). When set, <see cref="KeyPath"/> is required.</summary>
    public string? CertificatePath { get; set; }

    /// <summary>Operator-supplied private key (PEM, unencrypted PKCS#8 or PKCS#1/SEC1).</summary>
    public string? KeyPath { get; set; }

    /// <summary>Where the first-start self-signed certificate is persisted (outside the image).</summary>
    public string GeneratedCertificateDirectory { get; set; } = "/var/lib/goldmark/tls";

    /// <summary>Comma-separated DNS names and IP addresses for the self-signed certificate.</summary>
    public string Hostnames { get; set; } = "localhost";

    public HstsSettings Hsts { get; set; } = new();

    public bool UsesOperatorCertificate => !string.IsNullOrWhiteSpace(CertificatePath);

    public IReadOnlyList<string> HostnameList =>
        Hostnames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public sealed class HstsSettings
{
    /// <summary>
    /// <c>null</c> (default) = automatic: on with an operator-supplied certificate, off with the
    /// generated self-signed certificate (HSTS would make certificate warnings impossible to bypass).
    /// </summary>
    public bool? Enabled { get; set; }

    public int MaxAgeDays { get; set; } = 365;

    public bool IncludeSubDomains { get; set; }
}

internal static class HstsPolicy
{
    public static bool IsEnabled(HttpsOptions options) => options.Hsts.Enabled ?? options.UsesOperatorCertificate;
}

internal sealed partial class HttpsOptionsValidator : IValidateOptions<HttpsOptions>
{
    [GeneratedRegex(@"^(?=.{1,253}$)[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex DnsName();

    public ValidateOptionsResult Validate(string? name, HttpsOptions options)
    {
        var failures = new List<string>();
        if (options.Port is < 1 or > 65535)
        {
            failures.Add($"{HttpsOptions.SectionName}:Port must be between 1 and 65535.");
        }

        var hasCert = !string.IsNullOrWhiteSpace(options.CertificatePath);
        var hasKey = !string.IsNullOrWhiteSpace(options.KeyPath);
        if (hasCert != hasKey)
        {
            failures.Add($"{HttpsOptions.SectionName}:CertificatePath and KeyPath must be set together.");
        }

        if (!hasCert)
        {
            if (string.IsNullOrWhiteSpace(options.GeneratedCertificateDirectory))
            {
                failures.Add($"{HttpsOptions.SectionName}:GeneratedCertificateDirectory is required when no certificate is supplied.");
            }

            if (options.HostnameList.Count == 0)
            {
                failures.Add($"{HttpsOptions.SectionName}:Hostnames must list at least one name for the generated certificate.");
            }

            foreach (var host in options.HostnameList)
            {
                if (!IPAddress.TryParse(host, out _) && !DnsName().IsMatch(host))
                {
                    failures.Add($"{HttpsOptions.SectionName}:Hostnames contains an invalid DNS name or IP address.");
                    break;
                }
            }
        }

        if (options.Hsts.MaxAgeDays is < 1 or > 730)
        {
            failures.Add($"{HttpsOptions.SectionName}:Hsts:MaxAgeDays must be between 1 and 730.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

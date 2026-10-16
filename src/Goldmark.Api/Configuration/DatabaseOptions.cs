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

        if (options.ServerCertificatePath is { Length: > 0 } path && !File.Exists(path))
        {
            failures.Add($"{DatabaseOptions.SectionName}:ServerCertificatePath does not point to a readable file.");
        }

        if (options.ConnectTimeoutSeconds is < 1 or > 120)
        {
            failures.Add($"{DatabaseOptions.SectionName}:ConnectTimeoutSeconds must be between 1 and 120.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

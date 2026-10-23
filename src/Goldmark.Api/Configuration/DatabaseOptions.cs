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
    public const string SqlAuthentication = "Sql";
    public const string WindowsAuthentication = "Windows";

    public string Host { get; set; } = "";
    public int Port { get; set; } = 1433;
    public string Name { get; set; } = "Goldmark";

    /// <summary>
    /// <c>Sql</c> (default): a SQL login with <see cref="User"/> and <see cref="Password"/>. <c>Windows</c>
    /// (the Windows service, DEC-043): the service connects as its own account, which
    /// <c>Goldmark:Service:Account</c> names for the commands; no name or password is configured.
    /// </summary>
    public string Authentication { get; set; } = SqlAuthentication;

    /// <summary>
    /// The application checks its own rights in SQL Server at each start and refuses to start if it holds
    /// more than <c>goldmark_runtime</c> (the Windows service sets it: nobody else can sign in as the
    /// service account, and an administrator's impersonation misses rights from Windows groups, DEC-043
    /// item 11). Until the check passes, readiness fails.
    /// </summary>
    public bool CheckRightsAtStart { get; set; }

    public bool UsesWindowsAuthentication => Authentication == WindowsAuthentication;

    /// <summary>Runtime login. Must be a least-privilege login, never <c>sa</c>.</summary>
    public string User { get; set; } = "";
    public string Password { get; set; } = "";

    /// <summary>
    /// Optional path of the runtime login's credential file (<see cref="Configuration.CredentialFile"/>),
    /// which <c>bootstrap-db</c> writes. When set, the login's name and password are read from it;
    /// <see cref="User"/> and <see cref="Password"/>, if also set, must match it.
    /// </summary>
    public string? CredentialFile { get; set; }

    /// <summary>Why <see cref="CredentialFile"/> could not be applied (reported by the validator), or null.</summary>
    internal string? CredentialFileProblem { get; set; }

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

        if (options.Authentication is not (DatabaseOptions.SqlAuthentication or DatabaseOptions.WindowsAuthentication))
        {
            failures.Add($"{DatabaseOptions.SectionName}:Authentication must be '{DatabaseOptions.SqlAuthentication}' or '{DatabaseOptions.WindowsAuthentication}'.");
        }
        else if (options.UsesWindowsAuthentication)
        {
            if (!string.IsNullOrEmpty(options.User) || !string.IsNullOrEmpty(options.Password) || !string.IsNullOrWhiteSpace(options.CredentialFile))
            {
                failures.Add($"{DatabaseOptions.SectionName}:User, Password and CredentialFile must be empty with Authentication " +
                    $"'{DatabaseOptions.WindowsAuthentication}': the service connects as its own Windows account.");
            }
        }
        else if (options.CredentialFileProblem is { } problem)
        {
            failures.Add(problem);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(options.User))
            {
                failures.Add($"{DatabaseOptions.SectionName}:User is required (or {DatabaseOptions.SectionName}:CredentialFile).");
            }
            else if (string.Equals(options.User, "sa", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"{DatabaseOptions.SectionName}:User must be a least-privilege login, not 'sa'.");
            }

            if (string.IsNullOrEmpty(options.Password))
            {
                failures.Add($"{DatabaseOptions.SectionName}:Password is required (there is no default password), or {DatabaseOptions.SectionName}:CredentialFile.");
            }
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
    /// Replaces <see cref="DatabaseOptions.User"/> and <see cref="DatabaseOptions.Password"/> with the
    /// contents of <see cref="DatabaseOptions.CredentialFile"/>, if one is configured, or records why
    /// it cannot be used. Settings that are also given must match the file.
    /// </summary>
    public static void ApplyCredentialFile(DatabaseOptions options, bool protectPlain = false)
    {
        if (CredentialFile.Resolve(DatabaseOptions.SectionName, options.CredentialFile, options.User, options.Password, out var credential) is { } problem)
        {
            options.CredentialFileProblem = problem;
            return;
        }

        if (protectPlain && !string.IsNullOrWhiteSpace(options.CredentialFile)
            && CredentialFile.TryRead(options.CredentialFile, out var stored, out _, out var protection)
            && protection == Security.CredentialProtection.Plain)
        {
            // Written by bootstrap-db or rotate-db-credentials, which couldn't encrypt it to this account.
            try
            {
                CredentialFile.Write(options.CredentialFile, stored);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                options.CredentialFileProblem = $"{DatabaseOptions.SectionName}:CredentialFile could not be encrypted to this account ({ex.GetType().Name}).";
                return;
            }
        }

        options.User = credential.User;
        options.Password = credential.Password;
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

/// <summary>
/// On Windows, the runtime credential file's folder must be owner-only at every start (DEC-043 item 5, #6), not only
/// when a plain password is encrypted into it (Codex on #39). On Unix the file is written 0600 in a 0700 folder.
/// </summary>
internal sealed class CredentialFolderValidator(IOptions<ServiceOptions> service) : IValidateOptions<DatabaseOptions>
{
    public ValidateOptionsResult Validate(string? name, DatabaseOptions options) =>
        OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(options.CredentialFile)
            && Path.GetDirectoryName(options.CredentialFile) is { Length: > 0 } folder
            && Security.PrivateDirectory.Problem($"{DatabaseOptions.SectionName}:CredentialFile's folder", folder, service.Value.Account) is { } problem
            ? ValidateOptionsResult.Fail(problem)
            : ValidateOptionsResult.Success;
}

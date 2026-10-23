using System.Security.Cryptography;
using System.Text.Json;
using Goldmark.Api.Security;

namespace Goldmark.Api.Configuration;

/// <summary>A SQL login's name and password, as stored in a credential file.</summary>
internal sealed record LoginCredential(string User, string Password);

/// <summary>
/// Credential files (slice 2c, DEC-034): one JSON file per login, <c>{"user": ..., "password": ...}</c>,
/// written by <c>bootstrap-db</c> and read by <c>migrate</c> and the application. The path comes
/// from settings. On Unix the file is created 0600 in a 0700 directory; on Windows its folder is
/// owner-only (<see cref="PrivateDirectory"/>) and the file is encrypted to the service account
/// (<see cref="CredentialProtection"/>, DEC-043 items 5 and 10). It is replaced atomically. Its
/// contents never appear in messages: a problem names the file's setting only.
/// </summary>
internal static class CredentialFile
{
    public const int MinimumPasswordLength = 12;
    public const int MaximumPasswordLength = 128; // QUOTENAME input limit; longer input would yield NULL.
    public const int GeneratedPasswordLength = 32;
    public const int GeneratedSuffixLength = 10;

    private const string SuffixAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
    private const string PasswordAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const long MaximumFileSize = 16384; // an encrypted file is larger than the plain one
    private const string FolderSetting = "The credential file's folder";

    /// <summary>
    /// The problem when the password is encrypted to another account (the service account, read by an
    /// operator's command). <see cref="TryRead(string, out LoginCredential, out string, out CredentialProtection)"/>
    /// still returns the login's name.
    /// </summary>
    public const string NotForThisAccount = "is encrypted to another Windows account (the service account) and can't be read by this one";

    /// <summary>A generated login name: <c>goldmark_&lt;purpose&gt;_</c> and 10 random lowercase letters and digits.</summary>
    public static string GenerateName(string purpose) =>
        $"goldmark_{purpose}_{RandomNumberGenerator.GetString(SuffixAlphabet, GeneratedSuffixLength)}";

    /// <summary>
    /// 32 random letters and digits with at least one uppercase letter, lowercase letter and digit,
    /// so it meets SQL Server's password policy and needs no quoting anywhere.
    /// </summary>
    public static string GeneratePassword()
    {
        while (true)
        {
            var password = RandomNumberGenerator.GetString(PasswordAlphabet, GeneratedPasswordLength);
            if (password.Any(char.IsAsciiLetterUpper) && password.Any(char.IsAsciiLetterLower) && password.Any(char.IsAsciiDigit))
            {
                return password;
            }
        }
    }

    public static bool Exists(string path) => File.Exists(path);

    /// <summary>
    /// The login a reader (<c>migrate</c>, the application) uses: the credential file's when
    /// <paramref name="path"/> is set, and settings that are also given must match it (bootstrap-db
    /// rewrites the file from them first); otherwise the settings. Returns a problem naming the
    /// setting, or null.
    /// </summary>
    public static string? Resolve(string section, string? path, string user, string password, out LoginCredential credential)
    {
        // The configuration binder sets null for a setting that is present but empty.
        (user, password) = (user ?? "", password ?? "");
        credential = new LoginCredential(user, password);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (!TryRead(path, out var stored, out var problem))
        {
            return $"{section}:CredentialFile {problem}.";
        }

        if ((user.Length > 0 && !string.Equals(user, stored.User, StringComparison.Ordinal))
            || (password.Length > 0 && !string.Equals(password, stored.Password, StringComparison.Ordinal)))
        {
            return $"{section}:User and Password differ from {section}:CredentialFile. Set one or the other, or run bootstrap-db, which rewrites the file from them.";
        }

        credential = stored;
        return null;
    }

    /// <summary>
    /// Reads a credential file. On failure, <paramref name="problem"/> describes it without any
    /// of the file's contents ("does not exist", "is not a valid credential file", ...).
    /// </summary>
    public static bool TryRead(string path, out LoginCredential credential, out string problem) =>
        TryRead(path, out credential, out problem, out _);

    /// <summary>As above, and how the file was protected (<see cref="CredentialProtection.Plain"/> on Unix).</summary>
    public static bool TryRead(string path, out LoginCredential credential, out string problem, out CredentialProtection protection)
    {
        credential = new LoginCredential("", "");
        problem = "";
        protection = CredentialProtection.Plain;
        string text;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                problem = "does not point to an existing file";
                return false;
            }

            if (info.Length > MaximumFileSize)
            {
                problem = "is not a valid Goldmark credential file";
                return false;
            }

            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = "could not be read";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || root.EnumerateObject().Count() != 2
                || !root.TryGetProperty("user", out var user) || user.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("password", out var password) || password.ValueKind is not (JsonValueKind.String or JsonValueKind.Object)
                || !SqlIdentifier.IsValid(user.GetString()!))
            {
                problem = "is not a valid Goldmark credential file";
                return false;
            }

            // On Windows the password is encrypted to the service account (DEC-043 item 5); the login's
            // name is not a secret and stays readable, so a reader that can't decrypt still knows it.
            credential = new LoginCredential(user.GetString()!, "");
            var secret = password.ValueKind == JsonValueKind.String ? password.GetString() : null;
            if (password.ValueKind == JsonValueKind.Object)
            {
                if (!CredentialProtector.TryUnprotect(password, out secret, out protection))
                {
                    problem = protection == CredentialProtection.Plain ? "is not a valid Goldmark credential file" : NotForThisAccount;
                    return false;
                }
            }

            if (secret!.Length is < MinimumPasswordLength or > MaximumPasswordLength)
            {
                problem = "is not a valid Goldmark credential file";
                return false;
            }

            credential = credential with { Password = secret };
            return true;
        }
        catch (JsonException)
        {
            problem = "is not a valid Goldmark credential file";
            return false;
        }
    }

    /// <summary>
    /// Writes the file atomically (temporary file in the same directory, flushed to disk, then
    /// renamed over the old one) in an owner-only folder (<see cref="PrivateDirectory"/>). On Unix the
    /// file is created 0600. On Windows it is encrypted to <paramref name="serviceAccount"/>, or to this
    /// process's account when that is unset or the same; when Windows can't encrypt to another account
    /// (outside a domain), it is written plain and the service encrypts it at its next start (DEC-043
    /// item 10). Returns how it was protected.
    /// </summary>
    public static CredentialProtection Write(string path, LoginCredential credential, string? serviceAccount = null)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        PrivateDirectory.Ensure(FolderSetting, directory, serviceAccount);

        var protection = CredentialProtection.Plain;
        string? encrypted = null;
        if (OperatingSystem.IsWindows())
        {
            (encrypted, protection) = CredentialProtector.Protect(credential.Password, serviceAccount);
        }

        var json = Json(credential, protection, encrypted);
        var temp = path + ".tmp";
        File.Delete(temp);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = PrivateFileMode;
        }

        using (var stream = new FileStream(temp, options))
        {
            stream.Write(json);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temp, path, overwrite: true);
        return protection;
    }

    private static byte[] Json(LoginCredential credential, CredentialProtection protection, string? encrypted)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("user", credential.User);
            if (encrypted is null)
            {
                writer.WriteString("password", credential.Password);
            }
            else
            {
                writer.WriteStartObject("password");
                writer.WriteString("protection", CredentialProtector.Name(protection));
                writer.WriteString("encrypted", encrypted);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }
}

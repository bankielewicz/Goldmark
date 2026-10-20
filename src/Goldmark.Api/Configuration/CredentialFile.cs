using System.Security.Cryptography;
using System.Text.Json;

namespace Goldmark.Api.Configuration;

/// <summary>A SQL login's name and password, as stored in a credential file.</summary>
internal sealed record LoginCredential(string User, string Password);

/// <summary>
/// Credential files (slice 2c, DEC-034): one JSON file per login, <c>{"user": ..., "password": ...}</c>,
/// written by <c>bootstrap-db</c> and read by <c>migrate</c> and the application. The path comes
/// from settings, so another deployment can point it elsewhere (later a DPAPI-protected store).
/// On Unix the file is created 0600 in a 0700 directory and replaced atomically. Its contents
/// never appear in messages: a problem names the file's setting only.
/// </summary>
internal static class CredentialFile
{
    public const int MinimumPasswordLength = 12;
    public const int MaximumPasswordLength = 128; // QUOTENAME input limit; longer input would yield NULL.
    public const int GeneratedPasswordLength = 32;
    public const int GeneratedSuffixLength = 10;

    private const string SuffixAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
    private const string PasswordAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private const UnixFileMode DirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const long MaximumFileSize = 4096;

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
    public static bool TryRead(string path, out LoginCredential credential, out string problem)
    {
        credential = new LoginCredential("", "");
        problem = "";
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
                || !root.TryGetProperty("password", out var password) || password.ValueKind != JsonValueKind.String)
            {
                problem = "is not a valid Goldmark credential file";
                return false;
            }

            var value = new LoginCredential(user.GetString()!, password.GetString()!);
            if (!SqlIdentifier.IsValid(value.User) || value.Password.Length is < MinimumPasswordLength or > MaximumPasswordLength)
            {
                problem = "is not a valid Goldmark credential file";
                return false;
            }

            credential = value;
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
    /// renamed over the old one). On Unix the directory is forced to 0700 and the file is created 0600.
    /// </summary>
    public static void Write(string path, LoginCredential credential)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        EnsurePrivateDirectory(directory);

        var temp = path + ".tmp";
        File.Delete(temp);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = PrivateFileMode;
        }

        using (var stream = new FileStream(temp, options))
        {
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("user", credential.User);
                writer.WriteString("password", credential.Password);
                writer.WriteEndObject();
            }

            stream.Flush(flushToDisk: true);
        }

        File.Move(temp, path, overwrite: true);
    }

    private static void EnsurePrivateDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
            return;
        }

        Directory.CreateDirectory(directory, DirectoryMode);
        if (File.GetUnixFileMode(directory) != DirectoryMode)
        {
            // A volume mount point may have been created with a wider mode by the image build.
            File.SetUnixFileMode(directory, DirectoryMode);
        }
    }
}

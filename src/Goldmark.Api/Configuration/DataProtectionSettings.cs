using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Options;

namespace Goldmark.Api.Configuration;

/// <summary>
/// Where ASP.NET Core Data Protection keeps its key ring (configuration section
/// <c>Goldmark:DataProtection</c>; DEC-038 item 5). The keys protect the sign-in cookies, antiforgery
/// tokens, enrolment state and the stored authenticator keys. They are files in a directory from
/// settings (a volume only the app mounts in Docker; DPAPI-protected on the Windows service, slice
/// 4b) and must be backed up with the database: losing them signs everyone out and invalidates every
/// authenticator, so people sign in with a recovery code and set up two-factor again.
/// </summary>
public sealed class DataProtectionSettings
{
    public const string SectionName = "Goldmark:DataProtection";

    public string KeyDirectory { get; set; } = "/var/lib/goldmark/keys";
}

internal sealed class DataProtectionSettingsValidator : IValidateOptions<DataProtectionSettings>
{
    public ValidateOptionsResult Validate(string? name, DataProtectionSettings options) =>
        string.IsNullOrWhiteSpace(options.KeyDirectory)
            ? ValidateOptionsResult.Fail($"{DataProtectionSettings.SectionName}:KeyDirectory is required.")
            : ValidateOptionsResult.Success;
}

/// <summary>
/// The key ring as one XML file per key, like Data Protection's own file-system repository, except
/// that on Unix the directory is forced to 0700 and each file is created 0600 (as the credential
/// files, DEC-034), and a key is written to a temporary file and renamed, so a crash never leaves a
/// half-written key. Keys are not encrypted at rest (DEC-038 5A: a certificate's key would sit beside
/// them); Data Protection logs a warning saying so when it creates a key.
/// </summary>
internal sealed class PrivateKeyRingRepository(string directory) : IXmlRepository
{
    private const UnixFileMode DirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode FileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        EnsureDirectory();
        return [.. Directory.EnumerateFiles(directory, "*.xml").Order(StringComparer.Ordinal).Select(XElement.Load)];
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        EnsureDirectory();
        // The friendly name is "key-<guid>"; anything else gets a fresh name, never a path.
        var name = IsSafeName(friendlyName) ? friendlyName : $"key-{Guid.NewGuid():D}";
        var path = Path.Combine(directory, name + ".xml");
        var temp = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
        var options = new FileStreamOptions { Mode = System.IO.FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = FileMode;
        }

        using (var stream = new FileStream(temp, options))
        {
            element.Save(stream);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temp, path, overwrite: false);
    }

    private void EnsureDirectory()
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

    private static bool IsSafeName(string name) =>
        name.Length is > 0 and <= 64 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
}

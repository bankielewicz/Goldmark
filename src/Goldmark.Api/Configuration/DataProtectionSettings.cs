using System.Xml.Linq;
using Goldmark.Api.Security;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Options;

namespace Goldmark.Api.Configuration;

/// <summary>
/// Where ASP.NET Core Data Protection keeps its key ring (configuration section
/// <c>Goldmark:DataProtection</c>; DEC-038 item 5). The keys protect the sign-in cookies, antiforgery
/// tokens, enrolment state and the stored authenticator keys. They are files in a directory from
/// settings (a volume only the app mounts in Docker; on the Windows service an owner-only folder, the keys
/// encrypted to the service account, DEC-043 item 5) and must be backed up with the database: losing them
/// signs everyone out and invalidates every authenticator, so people sign in with a recovery code and set
/// up two-factor again. On Windows the backup is readable only by the same service account (item 5).
/// </summary>
public sealed class DataProtectionSettings
{
    public const string SectionName = "Goldmark:DataProtection";

    public string KeyDirectory { get; set; } = DefaultPaths.Data("keys");
}

/// <summary>
/// The key folder is required and owner-only. Checked at start (the folder is created if missing), so a
/// folder others can read stops the start with the setting's name rather than failing at first use.
/// </summary>
internal sealed class DataProtectionSettingsValidator(IOptions<ServiceOptions> service) : IValidateOptions<DataProtectionSettings>
{
    public ValidateOptionsResult Validate(string? name, DataProtectionSettings options) =>
        string.IsNullOrWhiteSpace(options.KeyDirectory)
            ? ValidateOptionsResult.Fail($"{DataProtectionSettings.SectionName}:KeyDirectory is required.")
            : PrivateDirectory.Problem($"{DataProtectionSettings.SectionName}:KeyDirectory", options.KeyDirectory, service.Value.Account) is { } problem
                ? ValidateOptionsResult.Fail(problem)
                : ValidateOptionsResult.Success;
}

/// <summary>
/// The key ring as one XML file per key, like Data Protection's own file-system repository, except
/// that the directory is owner-only (<see cref="PrivateDirectory"/>), on Unix each file is created 0600
/// (as the credential files, DEC-034), and a key is written to a temporary file and renamed, so a crash
/// never leaves a half-written key. On Unix keys are not encrypted at rest (DEC-038 5A: a certificate's
/// key would sit beside them) and Data Protection logs a warning saying so when it creates a key; on
/// Windows they are encrypted to the service account (<see cref="KeyRingEncryption"/>, DEC-043 item 5).
/// </summary>
internal sealed class PrivateKeyRingRepository(string directory, string? serviceAccount = null) : IXmlRepository
{
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

    private void EnsureDirectory() =>
        PrivateDirectory.Ensure($"{DataProtectionSettings.SectionName}:KeyDirectory", directory, serviceAccount);

    private static bool IsSafeName(string name) =>
        name.Length is > 0 and <= 64 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
}

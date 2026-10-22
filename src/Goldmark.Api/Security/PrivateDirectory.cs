using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Goldmark.Api.Security;

/// <summary>
/// A folder that holds secrets: credential files, the Data Protection key ring, the generated TLS key, the
/// log files. On Unix it is forced to 0700 (DEC-034). On Windows (DEC-043 item 5) a new folder gets a
/// protected ACL (inheritance off) for SYSTEM, Administrators, this process's account and the service
/// account (<c>Goldmark:Service:Account</c>); an existing folder that inherits its permissions, grants
/// anyone else access or has another owner is refused, never changed, so an operator decides.
/// </summary>
internal static class PrivateDirectory
{
    private const UnixFileMode Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>
    /// Creates the folder if needed and makes it (Unix) or checks it (Windows) owner-only. Returns why
    /// it is not, naming <paramref name="setting"/> and the folder but nothing in it, or null.
    /// </summary>
    public static string? Problem(string setting, string directory, string? serviceAccount)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return WindowsProblem(setting, directory, serviceAccount);
            }

            Directory.CreateDirectory(directory, Mode);
            if (File.GetUnixFileMode(directory) != Mode)
            {
                // A volume mount point may have been created with a wider mode by the image build.
                File.SetUnixFileMode(directory, Mode);
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"{setting} ({directory}) could not be created or made owner-only by this process ({ex.GetType().Name}).";
        }
    }

    /// <summary>
    /// On Windows, a file in such a folder: its own ACL decides who may open it, since every user may pass through
    /// the folder ("bypass traverse checking"), so a grant on the file to anyone else is refused like one on the
    /// folder (Codex on #39). Null on Unix, and when the file is private.
    /// </summary>
    public static string? FileProblem(string setting, string file, string? serviceAccount) =>
        OperatingSystem.IsWindows() ? WindowsFileProblem(setting, file, serviceAccount) : null;

    /// <summary>As <see cref="Problem"/>, for a writer: throws <see cref="IOException"/> with the problem.</summary>
    public static void Ensure(string setting, string directory, string? serviceAccount)
    {
        if (Problem(setting, directory, serviceAccount) is { } problem)
        {
            throw new IOException(problem);
        }
    }

    /// <summary>
    /// The service account's SID from <c>Goldmark:Service:Account</c> (an account name such as
    /// <c>CORP\svc-goldmark$</c> or <c>NT SERVICE\Goldmark</c>, or a SID), or a problem naming the setting.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static SecurityIdentifier? ServiceAccountSid(string? account, out string? problem)
    {
        problem = null;
        if (string.IsNullOrWhiteSpace(account))
        {
            return null;
        }

        try
        {
            return account.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase)
                ? new SecurityIdentifier(account)
                : (SecurityIdentifier)new NTAccount(account).Translate(typeof(SecurityIdentifier));
        }
        catch (Exception ex) when (ex is IdentityNotMappedException or ArgumentException)
        {
            problem = "Goldmark:Service:Account is not an account Windows knows on this computer or its domain.";
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? WindowsProblem(string setting, string directory, string? serviceAccount)
    {
        var account = ServiceAccountSid(serviceAccount, out var accountProblem);
        if (accountProblem is not null)
        {
            return accountProblem;
        }

        var allowed = Allowed(account);
        var folder = new DirectoryInfo(directory);
        if (!folder.Exists)
        {
            if (folder.Parent is { } parent)
            {
                parent.Create();
            }

            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            foreach (var sid in allowed)
            {
                security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            }

            folder.Create(security);
            return null;
        }

        var acl = folder.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        var fix = "Restrict it to SYSTEM, Administrators and the service account with inheritance off, or remove it and let Goldmark create it (docs/ops/windows-service.md).";
        if (!acl.AreAccessRulesProtected)
        {
            return $"{setting} ({directory}) inherits its permissions from its parent folder. {fix}";
        }

        if (acl.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !allowed.Contains(owner))
        {
            return $"{setting} ({directory}) is owned by an account other than SYSTEM, Administrators or the service account. {fix}";
        }

        foreach (FileSystemAccessRule rule in acl.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType == AccessControlType.Allow && !allowed.Contains((SecurityIdentifier)rule.IdentityReference))
            {
                return $"{setting} ({directory}) grants access to {Name((SecurityIdentifier)rule.IdentityReference)}. {fix}";
            }
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static string? WindowsFileProblem(string setting, string file, string? serviceAccount)
    {
        var account = ServiceAccountSid(serviceAccount, out var accountProblem);
        if (accountProblem is not null)
        {
            return accountProblem;
        }

        var allowed = Allowed(account);
        var fix = "Remove the other permissions from the file, or let the installer write it again (docs/ops/windows-service.md).";
        try
        {
            var acl = new FileInfo(file).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
            if (acl.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !allowed.Contains(owner))
            {
                return $"{setting} ({file}) is owned by an account other than SYSTEM, Administrators or the service account. {fix}";
            }

            foreach (FileSystemAccessRule rule in acl.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType == AccessControlType.Allow && !allowed.Contains((SecurityIdentifier)rule.IdentityReference))
                {
                    return $"{setting} ({file}) grants access to {Name((SecurityIdentifier)rule.IdentityReference)}. {fix}";
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"{setting} ({file}) could not be checked by this process ({ex.GetType().Name}).";
        }
    }

    /// <summary>SYSTEM, Administrators, this process's account and the service account, if set.</summary>
    [SupportedOSPlatform("windows")]
    private static HashSet<SecurityIdentifier> Allowed(SecurityIdentifier? serviceAccount)
    {
        var allowed = new HashSet<SecurityIdentifier>
        {
            new(WellKnownSidType.LocalSystemSid, null),
            new(WellKnownSidType.BuiltinAdministratorsSid, null),
        };
        using (var current = WindowsIdentity.GetCurrent())
        {
            if (current.User is { } user)
            {
                allowed.Add(user);
            }
        }

        if (serviceAccount is not null)
        {
            allowed.Add(serviceAccount);
        }

        return allowed;
    }

    [SupportedOSPlatform("windows")]
    private static string Name(SecurityIdentifier sid)
    {
        try
        {
            return sid.Translate(typeof(NTAccount)).Value;
        }
        catch (IdentityNotMappedException)
        {
            return sid.Value;
        }
    }
}

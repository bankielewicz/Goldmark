using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Goldmark.Api.Configuration;
using Goldmark.Api.Security;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;

namespace Goldmark.Api.Tests;

/// <summary>
/// Secrets at rest on Windows (DEC-043 items 5 and 10, #6): owner-only folders, the credential file's
/// password encrypted to the service account or left for the service to encrypt, and the key ring's
/// encryptor. Skipped elsewhere; the Windows CI job runs them.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsProtectionTests : IDisposable
{
    private const string Password = "Windows-Pa55word-NEVER-LOG-4b";
    private const string Setting = "Goldmark:Test:Folder";

    // NETWORK SERVICE stands in for a service account other than the account running the tests.
    private static readonly SecurityIdentifier OtherAccount = new(WellKnownSidType.NetworkServiceSid, null);

    private readonly string _root = Directory.CreateTempSubdirectory("goldmark-windows-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows ACLs and DPAPI only");
        }
    }

    [Fact]
    public void A_new_folder_gets_a_protected_ACL_for_SYSTEM_Administrators_and_this_account()
    {
        RequireWindows();
        var folder = Path.Combine(_root, "keys");

        Assert.Null(PrivateDirectory.Problem(Setting, folder, serviceAccount: null));

        var acl = new DirectoryInfo(folder).GetAccessControl();
        Assert.True(acl.AreAccessRulesProtected);
        using var me = WindowsIdentity.GetCurrent();
        var granted = Allowed(acl).ToHashSet();
        Assert.Equal(
            new HashSet<SecurityIdentifier> { new(WellKnownSidType.LocalSystemSid, null), new(WellKnownSidType.BuiltinAdministratorsSid, null), me.User! },
            granted);
    }

    [Fact]
    public void The_service_account_is_granted_and_allowed()
    {
        RequireWindows();
        var folder = Path.Combine(_root, "credentials");

        Assert.Null(PrivateDirectory.Problem(Setting, folder, OtherAccount.Value));

        Assert.Contains(OtherAccount, Allowed(new DirectoryInfo(folder).GetAccessControl()));
        // Another process without the setting sees an account it doesn't know of: refused, never changed.
        var problem = PrivateDirectory.Problem(Setting, folder, serviceAccount: null);
        Assert.NotNull(problem);
        Assert.Contains("grants access to NT AUTHORITY\\NETWORK SERVICE", problem, StringComparison.Ordinal);
        Assert.Contains(OtherAccount, Allowed(new DirectoryInfo(folder).GetAccessControl()));
    }

    [Fact]
    public void A_folder_that_inherits_its_permissions_is_refused_and_left_as_it_is()
    {
        RequireWindows();
        var folder = Directory.CreateDirectory(Path.Combine(_root, "inherits")).FullName;

        var problem = PrivateDirectory.Problem(Setting, folder, serviceAccount: null);

        Assert.NotNull(problem);
        Assert.StartsWith($"{Setting} ({folder}) inherits its permissions", problem, StringComparison.Ordinal);
        Assert.False(new DirectoryInfo(folder).GetAccessControl().AreAccessRulesProtected);
    }

    [Fact]
    public void A_protected_folder_that_grants_Users_is_refused()
    {
        RequireWindows();
        var folder = Path.Combine(_root, "users");
        Assert.Null(PrivateDirectory.Problem(Setting, folder, serviceAccount: null));
        var info = new DirectoryInfo(folder);
        var acl = info.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.Read, AccessControlType.Allow));
        info.SetAccessControl(acl);

        Assert.Contains("grants access to BUILTIN\\Users", PrivateDirectory.Problem(Setting, folder, serviceAccount: null), StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_service_account_is_named_by_its_setting()
    {
        RequireWindows();

        var problem = PrivateDirectory.Problem(Setting, Path.Combine(_root, "x"), "NO-SUCH-DOMAIN\\no-such-account$");

        Assert.Equal("Goldmark:Service:Account is not an account Windows knows on this computer or its domain.", problem);
    }

    [Fact]
    public void A_credential_file_for_this_account_is_encrypted_and_reads_back()
    {
        RequireWindows();
        var path = Path.Combine(_root, "own", "runtime.json");

        var protection = CredentialFile.Write(path, new LoginCredential("goldmark_app_aaaaaaaaaa", Password));

        Assert.NotEqual(CredentialProtection.Plain, protection);
        var text = File.ReadAllText(path);
        Assert.DoesNotContain(Password, text, StringComparison.Ordinal);
        Assert.Contains("\"user\":\"goldmark_app_aaaaaaaaaa\"", text, StringComparison.Ordinal);
        Assert.True(CredentialFile.TryRead(path, out var read, out _, out var readProtection));
        Assert.Equal(new LoginCredential("goldmark_app_aaaaaaaaaa", Password), read);
        Assert.Equal(protection, readProtection);
    }

    [Fact]
    public void A_credential_file_for_another_account_is_encrypted_to_it_or_left_for_the_service()
    {
        RequireWindows();
        var path = Path.Combine(_root, "other", "runtime.json");

        var protection = CredentialFile.Write(path, new LoginCredential("goldmark_app_bbbbbbbbbb", Password), OtherAccount.Value);

        if (protection == CredentialProtection.Plain)
        {
            // Outside a domain Windows refuses to encrypt to another account (measured, DEC-043 item 10):
            // the file stays plain in a folder only SYSTEM, Administrators and the service account reach.
            Assert.True(CredentialFile.TryRead(path, out var plain, out _));
            Assert.Equal(Password, plain.Password);
            Assert.Contains(OtherAccount, Allowed(new DirectoryInfo(Path.GetDirectoryName(path)!).GetAccessControl()));
        }
        else
        {
            Assert.Equal(CredentialProtection.DpapiNG, protection);
            Assert.False(CredentialFile.TryRead(path, out var named, out var problem));
            Assert.Equal(CredentialFile.NotForThisAccount, problem);
            Assert.Equal("goldmark_app_bbbbbbbbbb", named.User);
        }
    }

    [Fact]
    public void The_service_encrypts_a_plain_credential_file_to_itself_at_start()
    {
        RequireWindows();
        var path = Path.Combine(_root, "plain", "runtime.json");
        Assert.Null(PrivateDirectory.Problem(Setting, Path.GetDirectoryName(path)!, serviceAccount: null));
        File.WriteAllText(path, $$"""{"user":"goldmark_app_cccccccccc","password":"{{Password}}"}""");
        var options = new DatabaseOptions { CredentialFile = path };

        DatabaseOptionsValidator.ApplyCredentialFile(options, protectPlain: true);

        Assert.Null(options.CredentialFileProblem);
        Assert.Equal(("goldmark_app_cccccccccc", Password), (options.User, options.Password));
        Assert.DoesNotContain(Password, File.ReadAllText(path), StringComparison.Ordinal);
        Assert.True(CredentialFile.TryRead(path, out _, out _, out var protection));
        Assert.NotEqual(CredentialProtection.Plain, protection);
    }

    [Fact]
    public void An_unknown_protection_scheme_is_not_a_credential_file()
    {
        var path = Path.Combine(_root, "unknown.json");
        File.WriteAllText(path, """{"user":"goldmark_app_dddddddddd","password":{"protection":"rot13","encrypted":"abc"}}""");

        Assert.False(CredentialFile.TryRead(path, out _, out var problem));
        Assert.Equal("is not a valid Goldmark credential file", problem);
    }

    [Fact]
    public void The_key_ring_is_encrypted_to_this_account()
    {
        RequireWindows();
        var encryptor = KeyRingEncryption.ForCurrentAccount(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);

        Assert.True(encryptor is DpapiNGXmlEncryptor or DpapiXmlEncryptor, encryptor.GetType().Name);
    }

    [Fact]
    public void The_generated_TLS_folder_is_owner_only_on_a_dev_host() // #6
    {
        RequireWindows();
        var options = new HttpsOptions { GeneratedCertificateDirectory = Path.Combine(_root, ".goldmark", "tls") };
        var validator = new GeneratedCertificateFolderValidator(Microsoft.Extensions.Options.Options.Create(new ServiceOptions()));

        Assert.True(validator.Validate(null, options).Succeeded);

        Assert.True(new DirectoryInfo(options.GeneratedCertificateDirectory).GetAccessControl().AreAccessRulesProtected);
        var inherits = new HttpsOptions { GeneratedCertificateDirectory = Directory.CreateDirectory(Path.Combine(_root, "old-tls")).FullName };
        Assert.False(validator.Validate(null, inherits).Succeeded);
    }

    [Fact]
    public void The_credential_folder_is_checked_at_start_even_when_its_file_is_already_encrypted()
    {
        // Codex on #39: only a plain file's encryption checked the folder, so a widened folder went unnoticed.
        RequireWindows();
        var path = Path.Combine(_root, "credentials", "runtime.json");
        CredentialFile.Write(path, new LoginCredential("goldmark_app_eeeeeeeeee", Password));
        var validator = new CredentialFolderValidator(Microsoft.Extensions.Options.Options.Create(new ServiceOptions()));
        var options = new DatabaseOptions { CredentialFile = path };
        Assert.True(validator.Validate(null, options).Succeeded);
        var folder = new DirectoryInfo(Path.GetDirectoryName(path)!);
        var acl = folder.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.Read, AccessControlType.Allow));
        folder.SetAccessControl(acl);

        var result = validator.Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains("Goldmark:Database:CredentialFile's folder", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("grants access to BUILTIN\\Users", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_in_a_private_folder_that_grants_someone_else_is_refused()
    {
        // Codex on #39: the settings file's own ACL decides who reads it; every user may pass through its folder.
        RequireWindows();
        var folder = Path.Combine(_root, "settings");
        Assert.Null(PrivateDirectory.Problem(Setting, folder, serviceAccount: null));
        var file = Path.Combine(folder, "goldmark.json");
        File.WriteAllText(file, "{}");
        Assert.Null(PrivateDirectory.FileProblem(Setting, file, serviceAccount: null));
        var info = new FileInfo(file);
        var acl = info.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.Read, AccessControlType.Allow));
        info.SetAccessControl(acl);

        Assert.Contains("grants access to BUILTIN\\Users", PrivateDirectory.FileProblem(Setting, file, serviceAccount: null), StringComparison.Ordinal);
    }

    private static IEnumerable<SecurityIdentifier> Allowed(DirectorySecurity acl) =>
        acl.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
            .Where(r => r.AccessControlType == AccessControlType.Allow).Select(r => (SecurityIdentifier)r.IdentityReference).Distinct();
}

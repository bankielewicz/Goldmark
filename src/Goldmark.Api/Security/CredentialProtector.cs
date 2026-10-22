using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Logging.Abstractions;

namespace Goldmark.Api.Security;

/// <summary>How a credential file's password is stored.</summary>
internal enum CredentialProtection
{
    /// <summary>Not encrypted: Unix (0600 in a 0700 folder), or Windows until the service encrypts it.</summary>
    Plain,

    /// <summary>DPAPI-NG to an account's SID: that account can read it on any server of its domain.</summary>
    DpapiNG,

    /// <summary>Classic DPAPI to the current account: that account, on this server only.</summary>
    Dpapi,
}

/// <summary>
/// Encrypts a credential file's password on Windows (DEC-043 items 5 and 10) with Data Protection's own
/// DPAPI and DPAPI-NG encryptors, which ship in the framework. Measured on 2026-10-22: outside a domain
/// DPAPI-NG <c>SID=</c> refuses to encrypt, for this account or another, so a password for another
/// account (the service account, written by an operator's command) stays plain in its owner-only folder
/// until the service encrypts it to itself at its next start. Only the two decryptors below are ever
/// used; nothing in the file names a type.
/// </summary>
internal static class CredentialProtector
{
    private const string DpapiNGName = "dpapi-ng";
    private const string DpapiName = "dpapi";

    public static string Name(CredentialProtection protection) => protection switch
    {
        CredentialProtection.DpapiNG => DpapiNGName,
        CredentialProtection.Dpapi => DpapiName,
        _ => throw new ArgumentOutOfRangeException(nameof(protection)),
    };

    /// <summary>
    /// Encrypts <paramref name="password"/> to <paramref name="serviceAccount"/> (or to this process's
    /// account when that is unset or the same): DPAPI-NG when Windows allows it, else classic DPAPI for
    /// this account. Returns null and <see cref="CredentialProtection.Plain"/> when neither can reach the
    /// target account.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static (string? Encrypted, CredentialProtection Protection) Protect(string password, string? serviceAccount)
    {
        using var current = WindowsIdentity.GetCurrent();
        var me = current.User!;
        var target = PrivateDirectory.ServiceAccountSid(serviceAccount, out _) ?? me;
        var plain = new XElement("password", Convert.ToBase64String(Encoding.UTF8.GetBytes(password)));
        try
        {
            var encrypted = new DpapiNGXmlEncryptor($"SID={target.Value}", DpapiNGProtectionDescriptorFlags.None, NullLoggerFactory.Instance)
                .Encrypt(plain).EncryptedElement;
            return (Encode(encrypted), CredentialProtection.DpapiNG);
        }
        catch (CryptographicException)
        {
            // Outside a domain: fall through.
        }

        if (target == me)
        {
            var encrypted = new DpapiXmlEncryptor(protectToLocalMachine: false, NullLoggerFactory.Instance).Encrypt(plain).EncryptedElement;
            return (Encode(encrypted), CredentialProtection.Dpapi);
        }

        return (null, CredentialProtection.Plain);
    }

    /// <summary>
    /// Decrypts a password object (<c>{"protection": ..., "encrypted": ...}</c>). On failure,
    /// <paramref name="protection"/> is the scheme the file named when it was a known one (it was
    /// encrypted to another account), or <see cref="CredentialProtection.Plain"/> when the object isn't valid.
    /// </summary>
    public static bool TryUnprotect(JsonElement value, out string? password, out CredentialProtection protection)
    {
        password = null;
        protection = CredentialProtection.Plain;
        if (value.EnumerateObject().Count() != 2
            || !value.TryGetProperty("protection", out var scheme) || scheme.ValueKind != JsonValueKind.String
            || !value.TryGetProperty("encrypted", out var data) || data.ValueKind != JsonValueKind.String
            || !OperatingSystem.IsWindows())
        {
            return false;
        }

        IXmlDecryptor decryptor;
        switch (scheme.GetString())
        {
            case DpapiNGName:
                protection = CredentialProtection.DpapiNG;
                decryptor = new DpapiNGXmlDecryptor();
                break;
            case DpapiName:
                protection = CredentialProtection.Dpapi;
                decryptor = new DpapiXmlDecryptor();
                break;
            default:
                return false;
        }

        try
        {
            var element = XElement.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(data.GetString()!)));
            password = Encoding.UTF8.GetString(Convert.FromBase64String(decryptor.Decrypt(element).Value));
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or System.Xml.XmlException)
        {
            return false;
        }
    }

    private static string Encode(XElement encrypted) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(encrypted.ToString(SaveOptions.DisableFormatting)));
}

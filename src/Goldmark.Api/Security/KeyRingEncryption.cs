using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Logging.Abstractions;

namespace Goldmark.Api.Security;

/// <summary>
/// How the Windows service encrypts its Data Protection keys (DEC-043 items 5 and 10): DPAPI-NG to its own
/// account when Windows allows it (a domain account: a gMSA restored on another server of the domain can
/// still read them), otherwise classic DPAPI to its own account (the virtual account: this server only).
/// Either way only the service account reads them, so it is part of the backup.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class KeyRingEncryption
{
    public static IXmlEncryptor ForCurrentAccount(ILoggerFactory loggers)
    {
        using var current = WindowsIdentity.GetCurrent();
        var descriptor = $"SID={current.User!.Value}";
        try
        {
            // Measured on 2026-10-22: outside a domain, DPAPI-NG SID= refuses to encrypt. The probe logs
            // nowhere: the encryptor logs a failure as an error, and here it is an expected answer.
            var probe = new DpapiNGXmlEncryptor(descriptor, DpapiNGProtectionDescriptorFlags.None, NullLoggerFactory.Instance)
                .Encrypt(new XElement("probe")).EncryptedElement;
            new DpapiNGXmlDecryptor().Decrypt(probe);
            return new DpapiNGXmlEncryptor(descriptor, DpapiNGProtectionDescriptorFlags.None, loggers);
        }
        catch (CryptographicException)
        {
            return new DpapiXmlEncryptor(protectToLocalMachine: false, loggers);
        }
    }
}

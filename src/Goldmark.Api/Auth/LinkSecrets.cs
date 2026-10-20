using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Goldmark.Api.Auth;

/// <summary>
/// The secrets of invitation and two-factor reset links (DEC-038 items 2 and 8) and of the setup
/// token (item 7). Only hashes are stored: a link token is 256 random bits, so SHA-256 suffices;
/// the short code is hashed together with the token, so it cannot be tried without the token,
/// which is never stored.
/// </summary>
internal static class LinkSecrets
{
    /// <summary>As Identity's recovery codes: no 0/O, 1/I/L, so a code can be read aloud.</summary>
    public const string CodeAlphabet = "23456789BCDFGHJKMNPQRTVWXY";

    public const int CodeLength = 6;
    public const int TokenLength = 43; // 32 bytes, base64url without padding

    public static string NewToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>A code like <c>K7Q-2MX</c>.</summary>
    public static string NewCode()
    {
        var code = RandomNumberGenerator.GetString(CodeAlphabet, CodeLength);
        return $"{code[..3]}-{code[3..]}";
    }

    public static bool IsWellFormedToken(string? token) =>
        token is { Length: TokenLength } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>The code without spaces or hyphens, upper case; null unless it has the code's shape.</summary>
    public static string? NormalizeCode(string? code)
    {
        if (code is null || code.Length > 32)
        {
            return null;
        }

        var normalized = string.Concat(code.Where(c => c is not (' ' or '-'))).ToUpperInvariant();
        return normalized.Length == CodeLength && normalized.All(c => CodeAlphabet.Contains(c, StringComparison.Ordinal))
            ? normalized
            : null;
    }

    public static byte[] HashToken(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    /// <summary>SHA-256 of the token and the normalized code (<see cref="NormalizeCode"/>).</summary>
    public static byte[] HashCode(string token, string normalizedCode) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"{token}\n{normalizedCode}"));

    public static bool Matches(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual) =>
        CryptographicOperations.FixedTimeEquals(expected, actual);

    /// <summary>
    /// A setup token: 160 random bits as 32 base32 characters in groups of four, easy to copy from a
    /// log line. Matching ignores case, spaces and hyphens.
    /// </summary>
    public static string NewSetupToken()
    {
        var key = Base32.Encode(RandomNumberGenerator.GetBytes(20));
        return string.Join('-', Enumerable.Range(0, key.Length / 4).Select(i => key.Substring(i * 4, 4)));
    }

    public static byte[] HashSetupToken(string token) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(token.Where(c => c is not (' ' or '-'))).ToUpperInvariant()));
}

using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Goldmark.Api.Auth;

/// <summary>
/// Time-based one-time passwords (RFC 6238 over RFC 4226 HOTP, HMAC-SHA1, 30-second steps, 6 digits),
/// as authenticator apps produce them. Replaces the matching of Identity's authenticator provider,
/// which accepts two steps either side and the same code more than once (owner, DEC-040): a code is
/// accepted for the current step or one step either side (clock drift), and the caller records the
/// matched step so the same code is never accepted twice.
/// </summary>
internal static class Totp
{
    public const int Digits = 6;
    public const int StepSeconds = 30;

    private static readonly int[] Powers = [1, 10, 100, 1_000, 10_000, 100_000, 1_000_000, 10_000_000, 100_000_000];

    public static long TimeStep(DateTimeOffset now) => now.ToUnixTimeSeconds() / StepSeconds;

    /// <summary>The HOTP value for <paramref name="timeStep"/> (RFC 4226 section 5.3, dynamic truncation).</summary>
    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification =
        "RFC 6238 and the authenticator apps people use require HMAC-SHA1; HMAC-SHA1 is not affected by SHA-1 collisions.")]
    public static int Compute(ReadOnlySpan<byte> key, long timeStep, int digits = Digits)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(digits, 6);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(digits, 8);
        Span<byte> counter = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(counter, timeStep);
        Span<byte> hash = stackalloc byte[HMACSHA1.HashSizeInBytes];
        HMACSHA1.HashData(key, counter, hash);
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return binary % Powers[digits];
    }

    /// <summary>
    /// The newest time step among now - 1, now and now + 1 whose code equals <paramref name="code"/>,
    /// or null. All three are always computed and compared in constant time.
    /// </summary>
    public static long? Match(ReadOnlySpan<byte> key, string code, DateTimeOffset now)
    {
        if (!IsWellFormed(code))
        {
            return null;
        }

        var given = int.Parse(code, NumberStyles.None, CultureInfo.InvariantCulture);
        var current = TimeStep(now);
        long? matched = null;
        for (var delta = -1; delta <= 1; delta++)
        {
            if (FixedTimeEquals(Compute(key, current + delta), given))
            {
                matched = current + delta;
            }
        }

        return matched;
    }

    /// <summary>Exactly six ASCII digits; nothing else (no sign, spaces or other numerals).</summary>
    public static bool IsWellFormed(string? code) => code is { Length: Digits } && code.All(char.IsAsciiDigit);

    /// <summary>The otpauth:// URI an authenticator app reads from the QR code.</summary>
    public static string ProvisioningUri(string issuer, string userName, string base32Key) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(userName)}" +
        $"?secret={base32Key}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";

    /// <summary>The key as shown beside the QR code: groups of four, for typing into an app.</summary>
    public static string FormatKey(string base32Key)
    {
        var text = new StringBuilder(base32Key.Length + base32Key.Length / 4);
        for (var i = 0; i < base32Key.Length; i++)
        {
            if (i > 0 && i % 4 == 0)
            {
                text.Append(' ');
            }

            text.Append(base32Key[i]);
        }

        return text.ToString();
    }

    private static bool FixedTimeEquals(int a, int b)
    {
        Span<byte> left = stackalloc byte[sizeof(int)];
        Span<byte> right = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(left, a);
        BinaryPrimitives.WriteInt32BigEndian(right, b);
        return CryptographicOperations.FixedTimeEquals(left, right);
    }
}

/// <summary>RFC 4648 base32 (upper case, no padding), the encoding of authenticator keys.</summary>
internal static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var text = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                text.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            text.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return text.ToString();
    }

    /// <summary>Decodes upper-case base32 without padding; throws <see cref="FormatException"/> on anything else.</summary>
    public static byte[] Decode(string text)
    {
        var bytes = new List<byte>(text.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in text)
        {
            var value = Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (value < 0)
            {
                throw new FormatException("Not base32.");
            }

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        return [.. bytes];
    }
}

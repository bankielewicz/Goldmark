using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Primitives;

namespace Goldmark.Api.Tokens;

/// <summary>
/// An API token's secret (US-06, DEC-048): <c>sxt_</c>, so secret scanners recognise it, and 43 letters and digits,
/// the base62 form of 32 random bytes padded on the left. Goldmark keeps only the SHA-256 of the whole secret and its
/// last four characters (NFR-SEC-5); the secret itself is in no log, no audit entry and no response but the one that
/// creates the token (NFR-LOG-1). A request presents it as <c>Authorization: Bearer sxt_…</c>.
/// </summary>
internal static partial class ApiTokenSecret
{
    public const string Prefix = "sxt_";

    public const int RandomBytes = 32;

    /// <summary>Base62 digits for 32 bytes: 62^43 is just above 2^256, so 43 hold any value.</summary>
    public const int Digits = 43;

    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    /// <summary>The <c>Authorization</c> scheme a token is presented with (RFC 6750).</summary>
    public const string BearerScheme = "Bearer";

    public static string Create() => Prefix + Base62(RandomNumberGenerator.GetBytes(RandomBytes));

    /// <summary>32 bytes as a big-endian number in base62, always <see cref="Digits"/> characters.</summary>
    public static string Base62(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != RandomBytes)
        {
            throw new ArgumentException($"A token's secret is made of {RandomBytes} bytes.", nameof(bytes));
        }

        var value = new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
        var digits = new char[Digits];
        for (var i = Digits - 1; i >= 0; i--)
        {
            value = BigInteger.DivRem(value, Alphabet.Length, out var digit);
            digits[i] = Alphabet[(int)digit];
        }

        return new string(digits);
    }

    /// <summary>What the database keeps and looks a request's token up by: SHA-256 of the whole secret, prefix included.</summary>
    public static byte[] Hash(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));

    /// <summary>The last four characters, by which the screen and the audit log tell tokens apart.</summary>
    public static string LastFour(string secret) => secret[^4..];

    public static bool IsWellFormed([NotNullWhen(true)] string? text) => text is not null && Format().IsMatch(text);

    /// <summary>
    /// The secret of an <c>Authorization</c> header that holds exactly one bearer token in the secret's form (RFC 6750:
    /// the scheme in any case, then spaces); anything else is no token.
    /// </summary>
    public static bool TryRead(StringValues authorization, [NotNullWhen(true)] out string? secret)
    {
        secret = null;
        if (authorization.Count != 1 || authorization[0] is not { } value)
        {
            return false;
        }

        var space = value.IndexOf(' ', StringComparison.Ordinal);
        if (space != BearerScheme.Length || !value.AsSpan(0, space).Equals(BearerScheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var presented = value[space..].Trim(' ');
        if (!IsWellFormed(presented))
        {
            return false;
        }

        secret = presented;
        return true;
    }

    // \z, not $: in .NET, $ also matches before a final newline.
    [GeneratedRegex(@"^sxt_[0-9A-Za-z]{43}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Format();
}

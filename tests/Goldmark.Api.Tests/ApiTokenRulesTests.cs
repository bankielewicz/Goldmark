using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Goldmark.Api.Tests.Infrastructure;
using Goldmark.Api.Tokens;
using Microsoft.Extensions.Primitives;

namespace Goldmark.Api.Tests;

/// <summary>
/// API tokens' rules without a database (US-06, DEC-048): the secret and what Goldmark keeps of it, how a request
/// presents it, the expiry, the scopes, the names and the rate limit's setting.
/// </summary>
public sealed class ApiTokenRulesTests
{
    private const string Base62Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    [Fact]
    public void A_secret_is_sxt_and_43_letters_and_digits_and_each_one_is_new()
    {
        var secrets = Enumerable.Range(0, 500).Select(_ => ApiTokenSecret.Create()).ToList();

        Assert.All(secrets, s => Assert.Matches("^sxt_[0-9A-Za-z]{43}\\z", s));
        Assert.All(secrets, s => Assert.True(ApiTokenSecret.IsWellFormed(s)));
        Assert.Equal(secrets.Count, secrets.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void The_43_characters_are_the_32_random_bytes_in_base62_padded_on_the_left()
    {
        Assert.Equal(new string('0', 43), ApiTokenSecret.Base62(new byte[32]));
        Assert.Equal(new string('0', 42) + "z", ApiTokenSecret.Base62(Last(61)));
        Assert.Equal(new string('0', 41) + "10", ApiTokenSecret.Base62(Last(62)));

        var largest = ApiTokenSecret.Base62(Enumerable.Repeat((byte)0xFF, 32).ToArray());
        Assert.Equal(43, largest.Length);
        Assert.Equal(BigInteger.Pow(2, 256) - 1, Value(largest));

        var random = RandomNumberGenerator.GetBytes(32);
        Assert.Equal(new BigInteger(random, isUnsigned: true, isBigEndian: true), Value(ApiTokenSecret.Base62(random)));
        Assert.Throws<ArgumentException>(() => ApiTokenSecret.Base62(new byte[31]));
    }

    [Fact]
    public void Goldmark_keeps_only_the_SHA256_of_the_whole_secret_and_its_last_four_characters()
    {
        var secret = ApiTokenSecret.Create();

        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(secret)), ApiTokenSecret.Hash(secret));
        Assert.NotEqual(SHA256.HashData(Encoding.UTF8.GetBytes(secret["sxt_".Length..])), ApiTokenSecret.Hash(secret));
        Assert.Equal(secret[^4..], ApiTokenSecret.LastFour(secret));
    }

    [Theory]
    [InlineData("Bearer {0}", true)]
    [InlineData("bearer {0}", true)]
    [InlineData("BEARER   {0}", true)]
    [InlineData("Bearer {0} ", true)]
    [InlineData("Basic {0}", false)]
    [InlineData("Bearer{0}", false)]
    [InlineData("{0}", false)]
    [InlineData("Bearer {0}x", false)]
    [InlineData("Bearer {0}\n", false)]
    [InlineData("Bearer sxt_short", false)]
    [InlineData("Bearer sxt_test-token-NEVER-LOG-91c2", false)]
    [InlineData("Bearer {0},Bearer {0}", false)]
    [InlineData("Bearer ", false)]
    [InlineData("", false)]
    public void Only_a_bearer_token_in_the_secrets_form_is_read_from_the_header(string header, bool read)
    {
        var secret = ApiTokenSecret.Create();

        Assert.Equal(read, ApiTokenSecret.TryRead(new StringValues(string.Format(System.Globalization.CultureInfo.InvariantCulture, header, secret)), out var presented));
        Assert.Equal(read ? secret : null, presented);
    }

    [Theory]
    [InlineData("Bearer sxt_anything", true)]
    [InlineData("bearer whatever", true)]
    [InlineData("Bearer", true)]
    [InlineData("Basic Z29sZG1hcms6c2VjcmV0", false)]
    [InlineData("Negotiate YIIGhgYGKwYBBQUCoIIGejCCBnag", false)]
    [InlineData("BearerToken sxt_x", false)]
    [InlineData("", false)]
    public void Only_a_bearer_Authorization_header_makes_a_token_request_any_other_is_left_to_the_session(string header, bool bearer)
    {
        var request = new Microsoft.AspNetCore.Http.DefaultHttpContext().Request;
        request.Headers.Authorization = header;

        Assert.Equal(bearer, request.PresentsBearerToken());
        Assert.False(new Microsoft.AspNetCore.Http.DefaultHttpContext().Request.PresentsBearerToken());
    }

    [Fact]
    public void Two_authorization_headers_are_refused()
    {
        var secret = ApiTokenSecret.Create();

        Assert.False(ApiTokenSecret.TryRead(new StringValues(["Bearer " + secret, "Bearer " + secret]), out _));
        Assert.False(ApiTokenSecret.TryRead(StringValues.Empty, out _));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(180)]
    public void A_preset_counts_its_days_from_the_moment_the_token_is_created(int days)
    {
        var (expiry, errors) = TokenExpiry.Parse(days, null);
        var created = new DateTime(2026, 11, 22, 14, 5, 7, DateTimeKind.Utc).AddTicks(1_234_567);

        Assert.Empty(errors);
        Assert.Equal(created.AddDays(days), expiry!.ExpiresAt(created));
        Assert.Null(expiry.RangeError(DateOnly.FromDateTime(created)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(60)]
    [InlineData(365)]
    [InlineData(-30)]
    public void Only_30_90_and_180_days_are_presets(int days)
    {
        var (expiry, errors) = TokenExpiry.Parse(days, null);

        Assert.Null(expiry);
        Assert.Equal(["InvalidDays"], errors["expiresInDays"]);
    }

    [Fact]
    public void A_custom_date_lasts_to_the_end_of_that_day_in_UTC()
    {
        var (expiry, errors) = TokenExpiry.Parse(null, "2027-02-20");

        Assert.Empty(errors);
        Assert.Equal(new DateTime(2027, 2, 21, 0, 0, 0, DateTimeKind.Utc), expiry!.ExpiresAt(new DateTime(2026, 11, 22, 14, 5, 0, DateTimeKind.Utc)));
        Assert.Equal(DateTimeKind.Utc, expiry.ExpiresAt(DateTime.UtcNow).Kind);
    }

    [Theory]
    [InlineData("2026-11-21", "OutOfRange")]
    [InlineData("2026-11-22", "OutOfRange")]
    [InlineData("2026-11-23", null)]
    [InlineData("2027-11-22", null)]
    [InlineData("2027-11-23", "OutOfRange")]
    public void A_custom_date_is_from_tomorrow_to_365_days_ahead(string date, string? error)
    {
        var (expiry, errors) = TokenExpiry.Parse(null, date);

        Assert.Empty(errors);
        Assert.Equal(error, expiry!.RangeError(new DateOnly(2026, 11, 22)));
    }

    [Theory]
    [InlineData("2027-2-20")]
    [InlineData("20270220")]
    [InlineData("2027-02-30")]
    [InlineData("2027-02-20T00:00:00Z")]
    [InlineData(" 2027-02-20")]
    [InlineData("tomorrow")]
    public void A_custom_date_is_written_as_yyyy_MM_dd(string date)
    {
        var (expiry, errors) = TokenExpiry.Parse(null, date);

        Assert.Null(expiry);
        Assert.Equal(["InvalidDate"], errors["expiresOn"]);
    }

    [Fact]
    public void A_token_asks_for_exactly_one_kind_of_expiry()
    {
        Assert.Equal(["Required"], TokenExpiry.Parse(null, null).Errors["expires"]);
        Assert.Equal(["Required"], TokenExpiry.Parse(null, "").Errors["expires"]);
        Assert.Equal(["Ambiguous"], TokenExpiry.Parse(90, "2027-02-20").Errors["expires"]);
        Assert.Null(TokenExpiry.Parse(90, "2027-02-20").Expiry);
    }

    [Fact]
    public void The_scopes_are_reading_and_writing_CIs_and_relationships_and_nothing_else()
    {
        Assert.Equal(["cis:read", "cis:write", "relationships:read", "relationships:write"], TokenScopes.All);
        Assert.Equal(
            [TokenScopes.CisRead, TokenScopes.CisWrite, TokenScopes.RelationshipsRead, TokenScopes.RelationshipsWrite],
            TokenScopes.All);
    }

    [Fact]
    public void A_token_has_at_least_one_known_scope_each_once_in_the_lists_order()
    {
        var (scopes, error) = TokenScopes.Parse(["relationships:write", "cis:read", "cis:read"]);

        Assert.Null(error);
        Assert.Equal(["cis:read", "relationships:write"], scopes);
        Assert.Equal("Required", TokenScopes.Parse([]).Error);
        Assert.Equal("Required", TokenScopes.Parse(null).Error);
        Assert.Equal("UnknownScope", TokenScopes.Parse(["cis:read", "ingest:write"]).Error);
        Assert.Equal("UnknownScope", TokenScopes.Parse(["CIS:READ"]).Error);
        Assert.Equal("UnknownScope", TokenScopes.Parse(["cis:read", null]).Error);
    }

    [Theory]
    [InlineData(null, "Required")]
    [InlineData("", "Required")]
    [InlineData("   ", "Required")]
    [InlineData("owner sync", null)]
    [InlineData("  cmdb-export nightly  ", null)]
    [InlineData("owner\tsync", "InvalidText")]
    [InlineData("owner\nsync", "InvalidText")]
    public void A_name_is_required_and_has_no_control_characters(string? name, string? error) =>
        Assert.Equal(error, ApiTokenNames.Error(name));

    [Fact]
    public void A_name_is_at_most_100_characters_after_trimming_and_is_matched_ignoring_case_and_surrounding_spaces()
    {
        Assert.Null(ApiTokenNames.Error(new string('n', 100)));
        Assert.Null(ApiTokenNames.Error($"  {new string('n', 100)}  "));
        Assert.Equal("TooLong", ApiTokenNames.Error(new string('n', 101)));
        // A character that canonical normalization makes two counts as two in the stored form.
        Assert.Equal("TooLong", ApiTokenNames.Error(new string('\u0958', 51)));

        Assert.Equal("OWNER SYNC", ApiTokenNames.Normalize("  Owner Sync "));
        Assert.Equal(ApiTokenNames.Normalize("owner sync"), ApiTokenNames.Normalize("OWNER SYNC  "));
        Assert.Equal(ApiTokenNames.Normalize("Rene\u0301e"), ApiTokenNames.Normalize("Ren\u00e9e"));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(600, true)]
    [InlineData(100_000, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(100_001, false)]
    public void The_token_rate_limit_is_600_a_minute_unless_set_between_1_and_100000(int perMinute, bool valid)
    {
        var result = new ApiOptionsValidator().Validate(null, new ApiOptions { TokenRequestsPerMinute = perMinute });

        Assert.Equal(valid, result.Succeeded);
        Assert.All(result.Failures ?? [], f => Assert.StartsWith("Goldmark:Api:TokenRequestsPerMinute must be", f, StringComparison.Ordinal));
        Assert.Equal(600, new ApiOptions().TokenRequestsPerMinute);
    }

    [Fact]
    public async Task The_host_refuses_to_start_with_a_token_rate_limit_out_of_range()
    {
        await using var factory = new GoldmarkFactory();
        factory.Settings["Goldmark:Api:TokenRequestsPerMinute"] = "0";

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    private static byte[] Last(byte value)
    {
        var bytes = new byte[32];
        bytes[^1] = value;
        return bytes;
    }

    private static BigInteger Value(string digits) =>
        digits.Aggregate(BigInteger.Zero, (value, digit) => (value * 62) + Base62Digits.IndexOf(digit, StringComparison.Ordinal));
}

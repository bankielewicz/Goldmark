using System.Text;
using Goldmark.Api.Auth;
using Goldmark.Api.Users;
using Microsoft.Extensions.Options;

namespace Goldmark.Api.Tests;

/// <summary>TOTP (RFC 6238), base32, link secrets, the password list and the unknown-user throttle.</summary>
public sealed class AuthPrimitivesTests
{
    // RFC 6238 Appendix B, SHA1 rows: the seed is the ASCII string "12345678901234567890".
    [Theory]
    [InlineData(59L, 94287082)]
    [InlineData(1111111109L, 07081804)]
    [InlineData(1111111111L, 14050471)]
    [InlineData(1234567890L, 89005924)]
    [InlineData(2000000000L, 69279037)]
    [InlineData(20000000000L, 65353130)]
    public void Totp_matches_the_RFC_6238_test_vectors(long unixTime, int expected)
    {
        var key = Encoding.ASCII.GetBytes("12345678901234567890");

        Assert.Equal(expected, Totp.Compute(key, Totp.TimeStep(DateTimeOffset.FromUnixTimeSeconds(unixTime)), digits: 8));
        Assert.Equal(expected % 1_000_000, Totp.Compute(key, Totp.TimeStep(DateTimeOffset.FromUnixTimeSeconds(unixTime))));
    }

    [Fact]
    public void A_code_is_accepted_for_one_step_either_side_and_no_further()
    {
        var key = Encoding.ASCII.GetBytes("12345678901234567890");
        var now = DateTimeOffset.FromUnixTimeSeconds(1234567890);
        var step = Totp.TimeStep(now);
        string Code(long s) => Totp.Compute(key, s).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(step, Totp.Match(key, Code(step), now));
        Assert.Equal(step - 1, Totp.Match(key, Code(step - 1), now));
        Assert.Equal(step + 1, Totp.Match(key, Code(step + 1), now));
        Assert.Null(Totp.Match(key, Code(step - 2), now));
        Assert.Null(Totp.Match(key, Code(step + 2), now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("+12345")]
    [InlineData(" 123456")]
    [InlineData("12345a")]
    [InlineData("١٢٣٤٥٦")] // Arabic-Indic digits
    public void Only_six_ASCII_digits_are_a_code(string code) => Assert.False(Totp.IsWellFormed(code));

    [Fact]
    public void Base32_round_trips_and_rejects_other_characters()
    {
        var bytes = Enumerable.Range(0, 20).Select(i => (byte)(i * 13)).ToArray();

        Assert.Equal(bytes, Base32.Decode(Base32.Encode(bytes)));
        Assert.Equal("MZXW6YTBOI", Base32.Encode("foobar"u8)); // RFC 4648 section 10, without padding
        Assert.Throws<FormatException>(() => Base32.Decode("mzxw6ytboi"));
        Assert.Throws<FormatException>(() => Base32.Decode("MZXW6YTBOI======"));
    }

    [Fact]
    public void The_provisioning_uri_escapes_the_label()
    {
        var uri = Totp.ProvisioningUri("Goldmark", "sam rivera", "JBSWY3DPEHPK3PXP");

        Assert.Equal("otpauth://totp/Goldmark:sam%20rivera?secret=JBSWY3DPEHPK3PXP&issuer=Goldmark&algorithm=SHA1&digits=6&period=30", uri);
        Assert.Equal("JBSW Y3DP EHPK 3PXP", Totp.FormatKey("JBSWY3DPEHPK3PXP"));
    }

    [Fact]
    public void Link_tokens_are_256_bits_and_codes_avoid_look_alike_characters()
    {
        var token = LinkSecrets.NewToken();
        var code = LinkSecrets.NewCode();

        Assert.True(LinkSecrets.IsWellFormedToken(token));
        Assert.Equal(32, Convert.FromBase64String(token.Replace('-', '+').Replace('_', '/') + "=").Length);
        Assert.Matches("^[23456789BCDFGHJKMNPQRTVWXY]{3}-[23456789BCDFGHJKMNPQRTVWXY]{3}$", code);
        Assert.NotEqual(LinkSecrets.NewToken(), token);
    }

    [Theory]
    [InlineData("k7q-2mx", "K7Q2MX")]
    [InlineData(" K7Q 2MX ", "K7Q2MX")]
    [InlineData("K7Q2MX", "K7Q2MX")]
    [InlineData("K7Q-2M0", null)] // 0 is not in the alphabet
    [InlineData("K7Q-2M", null)]
    [InlineData("K7Q-2MXX", null)]
    public void Codes_are_normalized_for_typing(string typed, string? expected) => Assert.Equal(expected, LinkSecrets.NormalizeCode(typed));

    [Fact]
    public void A_code_hash_is_bound_to_its_token()
    {
        var token = LinkSecrets.NewToken();

        Assert.NotEqual(LinkSecrets.HashCode(token, "K7Q2MX"), LinkSecrets.HashCode(LinkSecrets.NewToken(), "K7Q2MX"));
        Assert.True(LinkSecrets.Matches(LinkSecrets.HashCode(token, "K7Q2MX"), LinkSecrets.HashCode(token, "K7Q2MX")));
    }

    [Fact]
    public void Setup_tokens_are_160_bits_and_match_ignoring_case_spaces_and_hyphens()
    {
        var token = LinkSecrets.NewSetupToken();

        Assert.Matches("^([A-Z2-7]{4}-){7}[A-Z2-7]{4}$", token);
        Assert.Equal(LinkSecrets.HashSetupToken(token), LinkSecrets.HashSetupToken(token.Replace("-", " ", StringComparison.Ordinal).ToLowerInvariant()));
        var state = new SetupState();
        state.Require(LinkSecrets.HashSetupToken(token));
        Assert.True(state.Verify(token));
        Assert.False(state.Verify(LinkSecrets.NewSetupToken()));
        state.Complete();
        Assert.False(state.Verify(token));
    }

    [Fact]
    public void The_common_password_list_is_bundled_and_matched_without_regard_to_case()
    {
        Assert.InRange(CommonPasswords.Count, 29_000, 31_000);
        Assert.True(CommonPasswords.Contains("password1234"));
        Assert.True(CommonPasswords.Contains("PassWord1234"));
        Assert.True(CommonPasswords.Contains("qwertyuiopas"));
        Assert.False(CommonPasswords.Contains("correct horse battery staple " + Guid.NewGuid()));
    }

    [Theory]
    [InlineData("abcdefghijk", null)]           // too short: Identity's own validator
    [InlineData("password1234", "PasswordCommon")]
    [InlineData("seven rabbits juggle quietly", null)]
    public async Task The_password_policy_refuses_common_and_overlong_passwords(string password, string? error)
    {
        var result = await new PasswordPolicy().ValidateAsync(null!, new GoldmarkUser(), password);

        Assert.Equal(error, result.Errors.Select(e => e.Code).SingleOrDefault());
        Assert.Contains(PasswordPolicy.TooLongCode,
            (await new PasswordPolicy().ValidateAsync(null!, new GoldmarkUser(), new string('x', 129))).Errors.Select(e => e.Code));
        Assert.True((await new PasswordPolicy().ValidateAsync(null!, new GoldmarkUser(), new string('x', 128))).Succeeded
            || CommonPasswords.Contains(new string('x', 128)));
    }

    [Theory]
    [InlineData("bcdfg-hjkmn", "BCDFG-HJKMN")]
    [InlineData("BCDFGHJKMN", "BCDFG-HJKMN")]
    [InlineData(" bcdfg hjkmn ", "BCDFG-HJKMN")]
    [InlineData("BCDFG-HJKM", null)]
    [InlineData("BCDFG-HJKM!", null)]
    public void Recovery_codes_are_normalized_like_Identity_writes_them(string typed, string? expected) =>
        Assert.Equal(expected, UserStore.NormalizeRecoveryCode(typed));

    [Fact]
    public void Unknown_user_names_lock_after_the_threshold_like_real_accounts()
    {
        var time = new ManualTime(DateTimeOffset.Parse("2026-10-20T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        using var throttle = new UnknownUserThrottle(Options.Create(new AuthOptions()), time);

        for (var i = 1; i < 5; i++)
        {
            Assert.False(throttle.RecordFailure("NOBODY"));
        }

        Assert.True(throttle.RecordFailure("NOBODY"));
        Assert.True(throttle.IsLockedOut("NOBODY"));
        Assert.False(throttle.IsLockedOut("SOMEONE"));
        time.Advance(TimeSpan.FromMinutes(15));
        Assert.False(throttle.IsLockedOut("NOBODY"));
    }

    [Fact]
    public void The_user_export_neutralizes_spreadsheet_formulas()
    {
        var user = new UserSummary(Guid.NewGuid(), "sam", "=HYPERLINK(\"http://x\")", ["Viewer"], true,
            new UserAccountState("active", null, null), null, DateTime.UtcNow, "e");

        var csv = UserDirectory.ToCsv([user]);

        Assert.Contains("\"'=HYPERLINK(\"\"http://x\"\")\"", csv, StringComparison.Ordinal);
        Assert.StartsWith("Name,Username,Roles,Two-factor,Account,Last sign-in (UTC)\r\n", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void The_QR_code_is_a_plain_SVG_path_with_no_script_or_style()
    {
        var svg = QrCodeImage.Svg(Totp.ProvisioningUri("Goldmark", "sam", "JBSWY3DPEHPK3PXP"));

        Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\"", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", svg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("style", svg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href", svg, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("LockoutThreshold", "0")]
    [InlineData("SessionIdleMinutes", "0")]
    [InlineData("SessionLifetimeHours", "200")]
    [InlineData("LinkExpiryHours", "0")]
    public void Auth_settings_out_of_range_are_refused(string setting, string value)
    {
        var options = new AuthOptions();
        typeof(AuthOptions).GetProperty(setting)!.SetValue(options, int.Parse(value, System.Globalization.CultureInfo.InvariantCulture));

        var result = new AuthOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains($"Goldmark:Auth:{setting}", result.FailureMessage, StringComparison.Ordinal);
    }

    private sealed class ManualTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}

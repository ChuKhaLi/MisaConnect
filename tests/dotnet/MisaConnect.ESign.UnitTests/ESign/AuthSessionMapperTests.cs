using System.Text.Json;
using MisaConnect.ESign.Infrastructure.ESign;
using MisaConnect.ESign.Infrastructure.ESign.Mapping;
using MisaConnect.ESign.Infrastructure.ESign.Wire;
using Xunit;

namespace MisaConnect.ESign.UnitTests.ESign;

public class AuthSessionMapperTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

    // null → default 300 s; explicit ≤ 0 honoured (already expired); 1–59 → 60 s floor; ≥ 60 as-is.
    public static TheoryData<int?, int> Cases => new()
    {
        { null, 300 },
        { 0, 0 },
        { -5, -5 },
        { 1, 60 },
        { 10, 60 },
        { 59, 60 },
        { 60, 60 },
        { 3600, 3600 },
    };

    private static LoginDataBlockDto Data(int? expiresIn) =>
        new() { AccessToken = "raw", RemoteSigningAccessToken = "rs", RefreshToken = "rt", ExpiresIn = expiresIn };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Login_envelope_lifetime(int? expiresIn, int expectedSeconds)
    {
        var session = AuthSessionMapper.FromLoginResponse(new LoginResponseDto { Data = Data(expiresIn) }, Now);
        Assert.Equal(Now.AddSeconds(expectedSeconds), session.ExpiresAtUtc);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Refresh_envelope_lifetime(int? expiresIn, int expectedSeconds)
    {
        var session = AuthSessionMapper.FromRefreshResponse(new RefreshTokenResponseDto { Data = Data(expiresIn) }, Now, "u", "alice");
        Assert.Equal(Now.AddSeconds(expectedSeconds), session.ExpiresAtUtc);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Refresh_flat_lifetime(int? expiresIn, int expectedSeconds)
    {
        var dto = new RefreshTokenResponseDto { AccessToken = "raw", RemoteSigningAccessToken = "rs", ExpiresIn = expiresIn };
        var session = AuthSessionMapper.FromRefreshResponse(dto, Now, "u", "alice");
        Assert.Equal(Now.AddSeconds(expectedSeconds), session.ExpiresAtUtc);
    }

    // Lenient reading: integer as-is; a string of an optional '-' and digits parsed;
    // anything else is treated as missing (→ 300 s by the lifetime rule), never a failed login.
    public static TheoryData<string, int?> RawExpiresIn => new()
    {
        { "3600", 3600 },
        { "\"3600\"", 3600 },
        { "\" 42 \"", 42 },
        { "\"-1\"", -1 },
        { "\"+42\"", null },
        { "3600.5", null },
        { "3600.0", null },
        { "\"abc\"", null },
        { "\"\"", null },
        { "true", null },
        { "{\"a\":1}", null },
        { "[1,2]", null },
        { "null", null },
        { "99999999999", null },
    };

    [Theory]
    [MemberData(nameof(RawExpiresIn))]
    public void Lenient_expiresIn_json_login_envelope(string raw, int? expected)
    {
        var json = "{\"data\":{\"remoteSigningAccessToken\":\"rs\",\"expiresIn\":" + raw + ",\"refreshToken\":\"rt\"}}";
        var dto = JsonSerializer.Deserialize<LoginResponseDto>(json, ESignJsonOptions.Wire);
        Assert.Equal(expected, dto!.Data!.ExpiresIn);
        Assert.Equal("rt", dto.Data.RefreshToken); // the reader stays aligned after skipping
    }

    [Theory]
    [MemberData(nameof(RawExpiresIn))]
    public void Lenient_expiresIn_json_refresh_flat(string raw, int? expected)
    {
        var json = "{\"remoteSigningAccessToken\":\"rs\",\"expiresIn\":" + raw + ",\"refreshToken\":\"rt\"}";
        var dto = JsonSerializer.Deserialize<RefreshTokenResponseDto>(json, ESignJsonOptions.Wire);
        Assert.Equal(expected, dto!.ExpiresIn);
        Assert.Equal("rt", dto.RefreshToken);
    }
}

using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Infrastructure.Caching;
using MisaConnect.ESign.UnitTests.TestSupport;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Authentication;

public class TwoFactorAuthExchangeTests
{
    private sealed class StaticKeySelector : ITokenCacheKeySelector
    {
        private readonly string _key;
        public StaticKeySelector(string key) { _key = key; }
        public string Compose() => _key;
    }

    private static AuthSession MakeSession() => new(
        AccessToken: "raw-2fa",
        RemoteSigningAccessToken: "rs-2fa",
        RefreshToken: "rt-2fa",
        ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(60),
        UserId: "user-id",
        Username: "alice");

    private static ExchangeOtp BuildExchange(StubWireClient wire, InMemoryTokenCache cache, ITokenCacheKeySelector selector) =>
        new(
            wire: wire,
            cache: cache,
            keySelector: selector,
            singleFlight: (key, factory, ct) => factory(ct));

    [Theory]
    [InlineData(OtpDeliveryChannel.SmsOrEmail, true)]
    [InlineData(OtpDeliveryChannel.SmsOrEmail, false)]
    [InlineData(OtpDeliveryChannel.Authenticator, true)]
    [InlineData(OtpDeliveryChannel.Authenticator, false)]
    public async Task Wire_body_carries_username_code_otpType_remember(OtpDeliveryChannel otpType, bool remember)
    {
        string? capturedUser = null;
        string? capturedCode = null;
        OtpDeliveryChannel? capturedOtpType = null;
        bool? capturedRemember = null;

        var wire = new StubWireClient
        {
            OnTwoFactorAuth = (u, c, t, r, _) =>
            {
                capturedUser = u;
                capturedCode = c;
                capturedOtpType = t;
                capturedRemember = r;
                return Task.FromResult(MakeSession());
            },
        };
        var cache = new InMemoryTokenCache();
        var selector = new StaticKeySelector("k");
        var sut = BuildExchange(wire, cache, selector);

        await sut.ExecuteAsync("alice", new OtpSubmission("123456", otpType, remember), CancellationToken.None);

        Assert.Equal("alice", capturedUser);
        Assert.Equal("123456", capturedCode);
        Assert.Equal(otpType, capturedOtpType);
        Assert.Equal(remember, capturedRemember);
    }

    [Fact]
    public async Task Successful_exchange_writes_cache_under_same_key_as_login_would()
    {
        var wire = new StubWireClient
        {
            OnTwoFactorAuth = (_, _, _, _, _) => Task.FromResult(MakeSession()),
        };
        var cache = new InMemoryTokenCache();
        var selector = new StaticKeySelector("login-key");
        var sut = BuildExchange(wire, cache, selector);

        var token = await sut.ExecuteAsync("alice", new OtpSubmission("123456", OtpDeliveryChannel.SmsOrEmail, false), CancellationToken.None);

        Assert.Equal(1, wire.TwoFactorAuthCalls);
        Assert.Equal("rs-2fa", token.Value);
        var cached = await cache.TryGetAsync("login-key", CancellationToken.None);
        Assert.NotNull(cached);
        Assert.Equal("rs-2fa", cached!.Value);
    }
}

using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Infrastructure.Caching;
using MisaConnect.ESign.Infrastructure.Http;
using MisaConnect.ESign.UnitTests.TestSupport;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Authentication;

public class TwoFactorAuthSingleFlightTests
{
    private sealed class StaticKeySelector : ITokenCacheKeySelector
    {
        private readonly string _key;
        public StaticKeySelector(string key) { _key = key; }
        public string Compose() => _key;
    }

    [Fact]
    public async Task Eight_concurrent_callers_fire_one_two_factor_request()
    {
        var twoFactorInvocations = 0;
        var gate = new TaskCompletionSource<AuthSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        var wire = new StubWireClient
        {
            OnTwoFactorAuth = (_, _, _, _, _) =>
            {
                Interlocked.Increment(ref twoFactorInvocations);
                return gate.Task;
            },
        };
        var cache = new InMemoryTokenCache();
        var selector = new StaticKeySelector("cache-key");
        var slot = new SingleFlightRefresh();
        var sut = new ExchangeOtp(
            wire: wire,
            cache: cache,
            keySelector: selector,
            singleFlight: (key, factory, ct) => slot.RefreshAsync(key, factory, ct));

        var pending = new List<Task<AccessToken>>();
        for (var i = 0; i < 8; i++)
        {
            pending.Add(sut.ExecuteAsync(
                "alice",
                new OtpSubmission("123456", OtpDeliveryChannel.SmsOrEmail, false),
                CancellationToken.None));
        }

        gate.SetResult(new AuthSession(
            AccessToken: "raw",
            RemoteSigningAccessToken: "rs-shared",
            RefreshToken: "rt",
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(60),
            UserId: "u",
            Username: "alice"));

        var results = await Task.WhenAll(pending);
        Assert.Equal(1, twoFactorInvocations);
        Assert.All(results, r => Assert.Equal("rs-shared", r.Value));
    }
}

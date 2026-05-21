using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Infrastructure.Caching;
using MisaConnect.ESign.UnitTests.TestSupport;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Authentication;

public class EnsureAccessTokenTests
{
    private sealed class StaticKeySelector : ITokenCacheKeySelector
    {
        private readonly string _key;
        public StaticKeySelector(string key) { _key = key; }
        public string Compose() => _key;
    }

    [Fact]
    public async Task Cold_cache_triggers_login_and_writes_cache()
    {
        var wire = new StubWireClient
        {
            OnLogin = (_, _, _) => Task.FromResult(new AuthSession(
                AccessToken: "raw",
                RemoteSigningAccessToken: "rs",
                RefreshToken: "rt",
                ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(60),
                UserId: "u1",
                Username: "user")),
        };
        var cache = new InMemoryTokenCache();
        var ensure = new EnsureAccessToken(
            wire: wire,
            cache: cache,
            keySelector: new StaticKeySelector("k"),
            clock: new FakeClock(DateTimeOffset.UtcNow),
            credentialsAccessor: () => ("user", "pass"),
            refreshUseCase: new RefreshAccessToken(wire));

        var token = await ensure.ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, wire.LoginCalls);
        Assert.Equal("rs", token.Value);
        var cached = await cache.TryGetAsync("k", CancellationToken.None);
        Assert.NotNull(cached);
        Assert.Equal("rs", cached!.Value);
    }

    [Fact]
    public async Task Hot_cache_reuses_without_login()
    {
        var wire = new StubWireClient
        {
            OnLogin = (_, _, _) => throw new InvalidOperationException("Should not be called when cache hot"),
        };
        var cache = new InMemoryTokenCache();
        var now = DateTimeOffset.UtcNow;
        await cache.SetAsync("k", new AccessToken("rs", "raw", "rt", now.AddMinutes(30), "u1", "user"), CancellationToken.None);

        var ensure = new EnsureAccessToken(
            wire: wire,
            cache: cache,
            keySelector: new StaticKeySelector("k"),
            clock: new FakeClock(now),
            credentialsAccessor: () => ("user", "pass"),
            refreshUseCase: new RefreshAccessToken(wire));

        var token = await ensure.ExecuteAsync(CancellationToken.None);

        Assert.Equal(0, wire.LoginCalls);
        Assert.Equal("rs", token.Value);
    }

    [Fact]
    public async Task Surfaces_122_without_propagating_password()
    {
        var wire = new StubWireClient
        {
            OnLogin = (u, _, _) => throw new AuthenticationFailedException(
                "122", "2fa required", "cid", requires2FA: true, username: u),
        };
        var cache = new InMemoryTokenCache();
        var ensure = new EnsureAccessToken(
            wire: wire,
            cache: cache,
            keySelector: new StaticKeySelector("k"),
            clock: new FakeClock(DateTimeOffset.UtcNow),
            credentialsAccessor: () => ("alice", "super-secret-password"),
            refreshUseCase: new RefreshAccessToken(wire));

        var ex = await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            ensure.ExecuteAsync(CancellationToken.None));

        Assert.True(ex.Requires2FA);
        Assert.Equal("alice", ex.Username);
        Assert.DoesNotContain("super-secret-password", ex.Detail);
        Assert.DoesNotContain("super-secret-password", ex.Message);
    }

    [Fact]
    public async Task Expired_cache_triggers_proactive_refresh()
    {
        var wire = new StubWireClient
        {
            OnRefresh = (_, _) => Task.FromResult(new AuthSession(
                AccessToken: "raw2",
                RemoteSigningAccessToken: "rs2",
                RefreshToken: "rt2",
                ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(60),
                UserId: "u1",
                Username: "user")),
        };
        var cache = new InMemoryTokenCache();
        var now = DateTimeOffset.UtcNow;
        await cache.SetAsync("k", new AccessToken("rs1", "raw1", "rt1", now.AddSeconds(-1), "u1", "user"), CancellationToken.None);

        var ensure = new EnsureAccessToken(
            wire: wire,
            cache: cache,
            keySelector: new StaticKeySelector("k"),
            clock: new FakeClock(now),
            credentialsAccessor: () => ("user", "pass"),
            refreshUseCase: new RefreshAccessToken(wire));

        var token = await ensure.ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, wire.RefreshCalls);
        Assert.Equal(0, wire.LoginCalls);
        Assert.Equal("rs2", token.Value);
    }
}

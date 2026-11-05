using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Infrastructure.Caching;
using MisaConnect.ESign.Infrastructure.Http;
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

    private static async Task<(StubWireClient wire, EnsureAccessToken ensure)> BuildWithTokenExpiringIn(TimeSpan remaining)
    {
        var now = DateTimeOffset.UtcNow;
        var wire = new StubWireClient
        {
            OnRefresh = (_, _) => Task.FromResult(new AuthSession("raw2", "rs2", "rt2", now.AddMinutes(60), "u1", "user")),
            OnLogin = (_, _, _) => throw new InvalidOperationException("login must not be called"),
        };
        var cache = new InMemoryTokenCache();
        await cache.SetAsync("k", new AccessToken("rs1", "raw1", "rt1", now + remaining, "u1", "user"), CancellationToken.None);
        var ensure = new EnsureAccessToken(
            wire: wire,
            cache: cache,
            keySelector: new StaticKeySelector("k"),
            clock: new FakeClock(now),
            credentialsAccessor: () => ("user", "pass"),
            refreshUseCase: new RefreshAccessToken(wire));
        return (wire, ensure);
    }

    [Fact]
    public async Task Token_with_exactly_30s_left_is_refreshed()
    {
        var (wire, ensure) = await BuildWithTokenExpiringIn(TimeSpan.FromSeconds(30));

        var token = await ensure.ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, wire.RefreshCalls);
        Assert.Equal(0, wire.LoginCalls);
        Assert.Equal("rs2", token.Value);
    }

    [Fact]
    public async Task Token_with_31s_left_is_reused()
    {
        var (wire, ensure) = await BuildWithTokenExpiringIn(TimeSpan.FromSeconds(31));

        var token = await ensure.ExecuteAsync(CancellationToken.None);

        Assert.Equal(0, wire.RefreshCalls);
        Assert.Equal(0, wire.LoginCalls);
        Assert.Equal("rs1", token.Value);
    }
    private static EnsureAccessToken BuildEnsure(
        StubWireClient wire,
        ITokenCache cache,
        DateTimeOffset now,
        SingleFlightRefresh? slot) =>
        new(
            wire: wire,
            cache: cache,
            keySelector: new StaticKeySelector("k"),
            clock: new FakeClock(now),
            credentialsAccessor: () => ("user", "pass"),
            refreshUseCase: new RefreshAccessToken(wire),
            singleFlight: slot is null ? null : slot.RefreshAsync);

    [Fact]
    public async Task Concurrent_proactive_refreshes_for_one_principal_coalesce()
    {
        var now = DateTimeOffset.UtcNow;
        var refreshes = 0;
        var gate = new TaskCompletionSource<AuthSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        var wire = new StubWireClient
        {
            OnRefresh = (_, _) =>
            {
                Interlocked.Increment(ref refreshes);
                return gate.Task;
            },
            OnLogin = (_, _, _) => throw new InvalidOperationException("login must not be called"),
        };
        var cache = new InMemoryTokenCache();
        await cache.SetAsync("k", new AccessToken("rs1", "raw1", "rt1", now.AddSeconds(10), "u1", "user"), CancellationToken.None);
        var slot = new SingleFlightRefresh();
        var first = BuildEnsure(wire, cache, now, slot);
        var second = BuildEnsure(wire, cache, now, slot);

        var t1 = first.ExecuteAsync(CancellationToken.None);
        var t2 = second.ExecuteAsync(CancellationToken.None);
        gate.SetResult(new AuthSession("raw2", "rs2", "rt2", now.AddMinutes(60), "u1", "user"));
        var results = await Task.WhenAll(t1, t2);

        Assert.Equal(1, refreshes);
        Assert.All(results, r => Assert.Equal("rs2", r.Value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refresh_returning_an_already_expired_token_falls_back_to_login(bool singleFlight)
    {
        var now = DateTimeOffset.UtcNow;
        var wire = new StubWireClient
        {
            OnRefresh = (_, _) => Task.FromResult(new AuthSession("raw2", "rs2", "rt2", now, "u1", "user")),
            OnLogin = (_, _, _) => Task.FromResult(new AuthSession("raw3", "rs3", "rt3", now.AddMinutes(60), "u1", "user")),
        };
        var cache = new InMemoryTokenCache();
        await cache.SetAsync("k", new AccessToken("rs1", "raw1", "rt1", now.AddSeconds(10), "u1", "user"), CancellationToken.None);
        var ensure = BuildEnsure(wire, cache, now, singleFlight ? new SingleFlightRefresh() : null);

        var token = await ensure.ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, wire.RefreshCalls);
        Assert.Equal(1, wire.LoginCalls);
        Assert.Equal("rs3", token.Value);
        Assert.Equal("rs3", (await cache.TryGetAsync("k", CancellationToken.None))!.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failing_refresh_does_not_evict_a_fresh_token_another_flow_stored(bool singleFlight)
    {
        var now = DateTimeOffset.UtcNow;
        var cache = new InMemoryTokenCache();
        await cache.SetAsync("k", new AccessToken("rs1", "raw1", "rt1", now.AddSeconds(10), "u1", "user"), CancellationToken.None);
        var wire = new StubWireClient
        {
            OnRefresh = async (_, ct) =>
            {
                // Another flow stores a fresh token while this refresh is failing (rotated refresh token).
                await cache.SetAsync("k", new AccessToken("rs-other", "raw-other", "rt-other", now.AddMinutes(60), "u1", "user"), ct);
                throw new AuthenticationFailedException("401", "refresh token already rotated", "cid", requires2FA: false);
            },
            // A 2FA principal: a login here would be a spurious 2FA failure.
            OnLogin = (u, _, _) => throw new AuthenticationFailedException("122", "2fa required", "cid", requires2FA: true, username: u),
        };
        var ensure = BuildEnsure(wire, cache, now, singleFlight ? new SingleFlightRefresh() : null);

        var token = await ensure.ExecuteAsync(CancellationToken.None);

        Assert.Equal("rs-other", token.Value);
        Assert.Equal(0, wire.LoginCalls);
        Assert.Equal("rs-other", (await cache.TryGetAsync("k", CancellationToken.None))!.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failing_refresh_evicts_the_stale_token_and_logs_in(bool singleFlight)
    {
        var now = DateTimeOffset.UtcNow;
        var wire = new StubWireClient
        {
            OnRefresh = (_, _) => throw new AuthenticationFailedException("401", "refresh rejected", "cid", requires2FA: false),
            OnLogin = (_, _, _) => Task.FromResult(new AuthSession("raw3", "rs3", "rt3", now.AddMinutes(60), "u1", "user")),
        };
        var cache = new InMemoryTokenCache();
        await cache.SetAsync("k", new AccessToken("rs1", "raw1", "rt1", now.AddSeconds(10), "u1", "user"), CancellationToken.None);
        var ensure = BuildEnsure(wire, cache, now, singleFlight ? new SingleFlightRefresh() : null);

        var token = await ensure.ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, wire.LoginCalls);
        Assert.Equal("rs3", token.Value);
    }
}

using System.Collections.Concurrent;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Infrastructure.Caching;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Caching;

public class TokenCacheRemoveIfMatchesTests
{
    /// <summary>A custom cache written against 2.6: only the three original members.</summary>
    private sealed class LegacyCache : ITokenCache
    {
        private readonly ConcurrentDictionary<string, AccessToken> _store = new();
        public Task<AccessToken?> TryGetAsync(string key, CancellationToken ct) =>
            Task.FromResult(_store.TryGetValue(key, out var t) ? t : null);
        public Task SetAsync(string key, AccessToken value, CancellationToken ct) { _store[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken ct) { _store.TryRemove(key, out _); return Task.CompletedTask; }
    }

    private static AccessToken Token(string value) =>
        new(value, "raw", "rt", DateTimeOffset.UtcNow.AddMinutes(60), "u", "alice");

    public static TheoryData<string> Caches => new() { "in-memory", "legacy" };

    private static ITokenCache Create(string kind) => kind == "legacy" ? new LegacyCache() : new InMemoryTokenCache();

    [Theory]
    [MemberData(nameof(Caches))]
    public async Task Matching_value_is_removed(string kind)
    {
        var cache = Create(kind);
        await cache.SetAsync("k", Token("T1"), CancellationToken.None);

        await cache.RemoveIfMatchesAsync("k", "T1", CancellationToken.None);

        Assert.Null(await cache.TryGetAsync("k", CancellationToken.None));
    }

    [Theory]
    [MemberData(nameof(Caches))]
    public async Task Different_value_is_kept(string kind)
    {
        var cache = Create(kind);
        await cache.SetAsync("k", Token("T9"), CancellationToken.None);

        await cache.RemoveIfMatchesAsync("k", "T1", CancellationToken.None);

        Assert.Equal("T9", (await cache.TryGetAsync("k", CancellationToken.None))?.Value);
    }

    [Theory]
    [MemberData(nameof(Caches))]
    public async Task Missing_key_is_a_no_op(string kind)
    {
        var cache = Create(kind);

        await cache.RemoveIfMatchesAsync("k", "T1", CancellationToken.None);

        Assert.Null(await cache.TryGetAsync("k", CancellationToken.None));
    }

    [Fact]
    public async Task In_memory_never_removes_a_value_stored_concurrently()
    {
        var cache = new InMemoryTokenCache();
        for (var i = 0; i < 2000; i++)
        {
            await cache.SetAsync("k", Token("stale"), CancellationToken.None);
            var remove = Task.Run(() => cache.RemoveIfMatchesAsync("k", "stale", CancellationToken.None));
            var store = Task.Run(() => cache.SetAsync("k", Token("fresh"), CancellationToken.None));
            await Task.WhenAll(remove, store);

            // Whatever the interleaving, the fresh token must survive.
            Assert.Equal("fresh", (await cache.TryGetAsync("k", CancellationToken.None))?.Value);
        }
    }
}

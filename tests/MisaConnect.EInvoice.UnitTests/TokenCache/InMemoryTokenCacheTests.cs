using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Infrastructure.Caching;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.TokenCache;

public class InMemoryTokenCacheTests
{
    [Fact]
    public async Task TryGet_miss_returns_null()
    {
        var cache = new InMemoryTokenCache();
        var v = await cache.TryGetAsync("missing", default);
        Assert.Null(v);
    }

    [Fact]
    public async Task Set_then_TryGet_returns_same()
    {
        var cache = new InMemoryTokenCache();
        var token = new AccessToken("abc", DateTimeOffset.UtcNow.AddDays(1));
        await cache.SetAsync("k", token, default);
        var read = await cache.TryGetAsync("k", default);
        Assert.NotNull(read);
        Assert.Equal("abc", read!.Value);
    }

    [Fact]
    public async Task Remove_clears_entry()
    {
        var cache = new InMemoryTokenCache();
        var token = new AccessToken("abc", DateTimeOffset.UtcNow.AddDays(1));
        await cache.SetAsync("k", token, default);
        await cache.RemoveAsync("k", default);
        var read = await cache.TryGetAsync("k", default);
        Assert.Null(read);
    }

    [Fact]
    public async Task Concurrent_Set_is_thread_safe()
    {
        var cache = new InMemoryTokenCache();
        var tasks = Enumerable.Range(0, 50).Select(i => Task.Run(() =>
            cache.SetAsync($"k{i % 5}", new AccessToken($"v{i}", DateTimeOffset.UtcNow.AddDays(1)), default))).ToArray();
        await Task.WhenAll(tasks);
        for (var i = 0; i < 5; i++)
        {
            Assert.NotNull(await cache.TryGetAsync($"k{i}", default));
        }
    }
}

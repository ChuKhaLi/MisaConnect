using System.Collections.Concurrent;
using MisaConnect.ESign.Application.Abstractions;

namespace MisaConnect.ESign.Infrastructure.Caching;

internal sealed class InMemoryTokenCache : ITokenCache
{
    private readonly ConcurrentDictionary<string, AccessToken> _store = new();

    public Task<AccessToken?> TryGetAsync(string key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Task.FromResult(_store.TryGetValue(key, out var token) ? token : null);
    }

    public Task SetAsync(string key, AccessToken value, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        _store[key] = value;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}

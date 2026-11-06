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

    /// <summary>Atomic: removes the exact stored entry only if its value still matches.</summary>
    public Task RemoveIfMatchesAsync(string key, string expectedValue, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        while (_store.TryGetValue(key, out var stored)
            && string.Equals(stored.Value, expectedValue, StringComparison.Ordinal))
        {
            // Compare-and-remove against the snapshot; if another write replaced it, re-check.
            if (_store.TryRemove(new KeyValuePair<string, AccessToken>(key, stored)))
            {
                break;
            }
        }
        return Task.CompletedTask;
    }
}

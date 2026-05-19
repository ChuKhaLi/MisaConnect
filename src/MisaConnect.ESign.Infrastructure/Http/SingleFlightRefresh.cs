using System.Collections.Concurrent;
using MisaConnect.ESign.Application.Abstractions;

namespace MisaConnect.ESign.Infrastructure.Http;

/// <summary>
/// Per-cache-key single-flight token refresh. At most one in-flight refresh
/// task per key — concurrent waiters await the same task and observe the
/// same outcome (success or exception). The slot is cleared on completion so
/// a follow-up 401 starts a fresh attempt.
/// </summary>
internal sealed class SingleFlightRefresh
{
    private readonly ConcurrentDictionary<string, Lazy<Task<AccessToken>>> _slots = new();

    public Task<AccessToken> RefreshAsync(string cacheKey, Func<CancellationToken, Task<AccessToken>> factory, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cacheKey);
        ArgumentNullException.ThrowIfNull(factory);

        Lazy<Task<AccessToken>>? installed = null;

        var slot = _slots.GetOrAdd(cacheKey, _ =>
        {
            installed = new Lazy<Task<AccessToken>>(() => factory(ct), LazyThreadSafetyMode.ExecutionAndPublication);
            return installed;
        });

        var task = slot.Value;

        if (ReferenceEquals(slot, installed))
        {
            _ = task.ContinueWith(completed =>
            {
                _slots.TryRemove(new KeyValuePair<string, Lazy<Task<AccessToken>>>(cacheKey, slot));
                GC.KeepAlive(completed);
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        return task;
    }
}

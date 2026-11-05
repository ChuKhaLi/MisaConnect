using System.Collections.Concurrent;
using MisaConnect.ESign.Application.Abstractions;

namespace MisaConnect.ESign.Infrastructure.Http;

/// <summary>
/// Per-cache-key single-flight token refresh. At most one in-flight refresh
/// task per key: concurrent waiters await the same task and observe the
/// same outcome (success or exception). The slot is cleared on completion so
/// a follow-up 401 starts a fresh attempt.
/// </summary>
/// <remarks>
/// The shared flight runs on <see cref="CancellationToken.None"/>, so the caller that
/// happened to start it cannot cancel it for the others. Each waiter observes only its
/// own token: a cancelled waiter gets <see cref="OperationCanceledException"/> while the
/// flight keeps running for the rest (bounded by the HTTP client's own timeout).
/// </remarks>
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
            installed = new Lazy<Task<AccessToken>>(() => factory(CancellationToken.None), LazyThreadSafetyMode.ExecutionAndPublication);
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

        return task.WaitAsync(ct);
    }
}

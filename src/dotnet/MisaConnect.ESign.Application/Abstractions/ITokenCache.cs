namespace MisaConnect.ESign.Application.Abstractions;

public interface ITokenCache
{
    Task<AccessToken?> TryGetAsync(string key, CancellationToken ct);
    Task SetAsync(string key, AccessToken value, CancellationToken ct);
    Task RemoveAsync(string key, CancellationToken ct);

    /// <summary>
    /// Removes the entry for <paramref name="key"/> only while its <see cref="AccessToken.Value"/>
    /// equals <paramref name="expectedValue"/> (ordinal), so a token another flow stored meanwhile
    /// is not evicted. The default implementation reads, compares and removes, which leaves a small
    /// race in which a just-stored token can be evicted; a cache that can compare-and-remove
    /// atomically should override it.
    /// </summary>
    async Task RemoveIfMatchesAsync(string key, string expectedValue, CancellationToken ct)
    {
        var current = await TryGetAsync(key, ct).ConfigureAwait(false);
        if (current is not null && string.Equals(current.Value, expectedValue, StringComparison.Ordinal))
        {
            await RemoveAsync(key, ct).ConfigureAwait(false);
        }
    }
}

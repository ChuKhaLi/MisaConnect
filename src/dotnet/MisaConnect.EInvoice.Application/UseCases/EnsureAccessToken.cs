using MisaConnect.EInvoice.Application.Abstractions;

namespace MisaConnect.EInvoice.Application.UseCases;

public sealed class EnsureAccessToken
{
    private static readonly TimeSpan SafetySkew = TimeSpan.FromHours(1);
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromDays(14);

    private readonly IMeInvoiceClient _client;
    private readonly ITokenCache _cache;
    private readonly ISystemClock _clock;
    private readonly Func<string> _cacheKeyFactory;

    public EnsureAccessToken(
        IMeInvoiceClient client,
        ITokenCache cache,
        ISystemClock clock,
        Func<string> cacheKeyFactory)
    {
        _client = client;
        _cache = cache;
        _clock = clock;
        _cacheKeyFactory = cacheKeyFactory;
    }

    public async Task<AccessToken> ExecuteAsync(CancellationToken ct)
    {
        var key = _cacheKeyFactory();
        var threshold = _clock.UtcNow + SafetySkew;

        var cached = await _cache.TryGetAsync(key, ct).ConfigureAwait(false);
        if (cached is not null && cached.ExpiresAtUtc > threshold)
        {
            return cached;
        }

        var fresh = await _client.AcquireTokenAsync(ct).ConfigureAwait(false);
        var finalExpiry = _clock.UtcNow + TokenLifetime - SafetySkew;
        var token = new AccessToken(fresh.Value, finalExpiry);
        await _cache.SetAsync(key, token, ct).ConfigureAwait(false);
        return token;
    }
}

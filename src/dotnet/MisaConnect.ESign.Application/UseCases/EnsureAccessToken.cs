using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Domain.Authentication;

namespace MisaConnect.ESign.Application.UseCases;

/// <summary>
/// Acquires an <see cref="AccessToken"/> either from the cache (when present
/// and not yet expired by the clock) or by invoking the MISA login endpoint.
/// US2 layers proactive-refresh and cache-write on top of this base.
/// </summary>
public sealed class EnsureAccessToken
{
    private readonly IMisaESignWireClient _wire;
    private readonly ITokenCache _cache;
    private readonly ITokenCacheKeySelector _keySelector;
    private readonly ISystemClock _clock;
    private readonly Func<(string userName, string password)> _credentialsAccessor;
    private readonly Func<(TimeSpan? proactiveRefreshSkew, string? refreshTokenOverride)> _refreshHintAccessor;
    private readonly RefreshAccessToken? _refreshUseCase;

    public EnsureAccessToken(
        IMisaESignWireClient wire,
        ITokenCache cache,
        ITokenCacheKeySelector keySelector,
        ISystemClock clock,
        Func<(string userName, string password)> credentialsAccessor,
        RefreshAccessToken? refreshUseCase = null)
    {
        _wire = wire;
        _cache = cache;
        _keySelector = keySelector;
        _clock = clock;
        _credentialsAccessor = credentialsAccessor;
        _refreshUseCase = refreshUseCase;
        _refreshHintAccessor = () => (TimeSpan.FromSeconds(30), null);
    }

    public async Task<AccessToken> ExecuteAsync(CancellationToken ct)
    {
        var key = _keySelector.Compose();
        var existing = await _cache.TryGetAsync(key, ct).ConfigureAwait(false);
        var now = _clock.UtcNow;

        if (existing is not null && existing.ExpiresAtUtc > now)
        {
            return existing;
        }

        if (existing is not null && _refreshUseCase is not null && !string.IsNullOrEmpty(existing.RefreshToken))
        {
            try
            {
                var refreshed = await _refreshUseCase.ExecuteAsync(existing.RefreshToken, existing.UserId, existing.Username, ct).ConfigureAwait(false);
                await _cache.SetAsync(key, refreshed, ct).ConfigureAwait(false);
                return refreshed;
            }
            catch
            {
                await _cache.RemoveAsync(key, ct).ConfigureAwait(false);
            }
        }

        var (userName, password) = _credentialsAccessor();
        var session = await _wire.LoginAsync(userName, password, ct).ConfigureAwait(false);
        var token = ToAccessToken(session);
        await _cache.SetAsync(key, token, ct).ConfigureAwait(false);
        return token;
    }

    internal static AccessToken ToAccessToken(AuthSession session) =>
        new(
            Value: session.RemoteSigningAccessToken,
            RawAccessToken: session.AccessToken,
            RefreshToken: session.RefreshToken,
            ExpiresAtUtc: session.ExpiresAtUtc,
            UserId: session.UserId,
            Username: session.Username);
}

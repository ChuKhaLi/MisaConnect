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
    /// <summary>A cached token with this much life left or less is refreshed rather than reused.</summary>
    public static readonly TimeSpan ProactiveRefreshSkew = TimeSpan.FromSeconds(30);

    private readonly IMisaESignWireClient _wire;
    private readonly ITokenCache _cache;
    private readonly ITokenCacheKeySelector _keySelector;
    private readonly ISystemClock _clock;
    private readonly Func<(string userName, string password)> _credentialsAccessor;
    private readonly RefreshAccessToken? _refreshUseCase;
    private readonly ExchangeOtp.SingleFlightDelegate? _singleFlight;

    /// <summary>
    /// Creates the use case without a shared refresh flight: concurrent proactive refreshes
    /// for one principal are not coalesced.
    /// </summary>
    public EnsureAccessToken(
        IMisaESignWireClient wire,
        ITokenCache cache,
        ITokenCacheKeySelector keySelector,
        ISystemClock clock,
        Func<(string userName, string password)> credentialsAccessor,
        RefreshAccessToken? refreshUseCase = null)
        : this(wire, cache, keySelector, clock, credentialsAccessor, refreshUseCase, singleFlight: null)
    {
    }

    /// <summary>
    /// Creates the use case with a shared refresh flight. Proactive refreshes run through
    /// <paramref name="singleFlight"/>, keyed by the token cache key, so concurrent refreshes
    /// for one principal (proactive and 401-driven) coalesce into one refresh call.
    /// </summary>
    /// <param name="wire">MISA wire client used for login.</param>
    /// <param name="cache">Token cache.</param>
    /// <param name="keySelector">Composes the cache key for the current principal.</param>
    /// <param name="clock">System clock.</param>
    /// <param name="credentialsAccessor">Returns the login credentials, invoked lazily.</param>
    /// <param name="refreshUseCase">Refresh use case; <see langword="null"/> disables refresh.</param>
    /// <param name="singleFlight">Shared single-flight runner; <see langword="null"/> refreshes without coalescing.</param>
    public EnsureAccessToken(
        IMisaESignWireClient wire,
        ITokenCache cache,
        ITokenCacheKeySelector keySelector,
        ISystemClock clock,
        Func<(string userName, string password)> credentialsAccessor,
        RefreshAccessToken? refreshUseCase,
        ExchangeOtp.SingleFlightDelegate? singleFlight)
    {
        _wire = wire;
        _cache = cache;
        _keySelector = keySelector;
        _clock = clock;
        _credentialsAccessor = credentialsAccessor;
        _refreshUseCase = refreshUseCase;
        _singleFlight = singleFlight;
    }

    public async Task<AccessToken> ExecuteAsync(CancellationToken ct)
    {
        var key = _keySelector.Compose();
        var existing = await _cache.TryGetAsync(key, ct).ConfigureAwait(false);

        if (existing is not null && IsFresh(existing, _clock.UtcNow))
        {
            return existing;
        }

        if (existing is not null && _refreshUseCase is not null && !string.IsNullOrEmpty(existing.RefreshToken))
        {
            try
            {
                return _singleFlight is null
                    ? await RefreshInsideFlightAsync(key, _refreshUseCase, ct).ConfigureAwait(false)
                    : await _singleFlight(
                        key,
                        innerCt => RefreshInsideFlightAsync(key, _refreshUseCase, innerCt),
                        ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
            {
                // Never evict a token another flow stored meanwhile: reuse it if it is fresh,
                // drop the entry only while it still holds the stale token, then log in.
                var current = await _cache.TryGetAsync(key, ct).ConfigureAwait(false);
                if (current is not null && IsFresh(current, _clock.UtcNow))
                {
                    return current;
                }
                if (current is not null && string.Equals(current.Value, existing.Value, StringComparison.Ordinal))
                {
                    await _cache.RemoveAsync(key, ct).ConfigureAwait(false);
                }
            }
        }

        var (userName, password) = _credentialsAccessor();
        var session = await _wire.LoginAsync(userName, password, ct).ConfigureAwait(false);
        var token = ToAccessToken(session);
        await _cache.SetAsync(key, token, ct).ConfigureAwait(false);
        return token;
    }

    /// <summary>
    /// Runs inside the shared flight (when one is configured): re-reads the cache so a token
    /// another flow already refreshed is reused, otherwise refreshes and stores the result.
    /// A refresh result that is already expired counts as a failed refresh.
    /// </summary>
    private async Task<AccessToken> RefreshInsideFlightAsync(string key, RefreshAccessToken refreshUseCase, CancellationToken ct)
    {
        var cached = await _cache.TryGetAsync(key, ct).ConfigureAwait(false);
        if (cached is not null && IsFresh(cached, _clock.UtcNow))
        {
            return cached;
        }
        if (cached is null || string.IsNullOrEmpty(cached.RefreshToken))
        {
            throw new InvalidOperationException("No cached refresh token is available.");
        }
        var refreshed = await refreshUseCase.ExecuteAsync(cached.RefreshToken, cached.UserId, cached.Username, ct).ConfigureAwait(false);
        if (refreshed.ExpiresAtUtc <= _clock.UtcNow)
        {
            throw new InvalidOperationException("Refresh returned an already-expired token.");
        }
        await _cache.SetAsync(key, refreshed, ct).ConfigureAwait(false);
        return refreshed;
    }

    private static bool IsFresh(AccessToken token, DateTimeOffset now) =>
        token.ExpiresAtUtc - ProactiveRefreshSkew > now;

    internal static AccessToken ToAccessToken(AuthSession session) =>
        new(
            Value: session.RemoteSigningAccessToken,
            RawAccessToken: session.AccessToken,
            RefreshToken: session.RefreshToken,
            ExpiresAtUtc: session.ExpiresAtUtc,
            UserId: session.UserId,
            Username: session.Username);
}

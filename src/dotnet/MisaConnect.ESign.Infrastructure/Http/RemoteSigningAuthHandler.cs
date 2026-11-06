using System.Net;
using Microsoft.Extensions.Options;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Infrastructure.Configuration;
using MisaConnect.ESign.Infrastructure.ESign;

namespace MisaConnect.ESign.Infrastructure.Http;

/// <summary>
/// On every request to a non-auth MISA endpoint: set the
/// <see cref="AuthorizationRM"/> header from the current token
/// (<see cref="EnsureAccessToken"/>), overriding whatever the caller set. On 401:
/// reuse a token another flow already refreshed, otherwise refresh through
/// <see cref="SingleFlightRefresh"/>, falling back to one fresh login when
/// refresh is unavailable or fails; retry the original request exactly once.
/// </summary>
internal sealed class RemoteSigningAuthHandler : DelegatingHandler
{
    private readonly Func<EnsureAccessToken> _ensureFactory;
    private readonly Func<RefreshAccessToken> _refreshFactory;
    private readonly ITokenCache _cache;
    private readonly ITokenCacheKeySelector _keySelector;
    private readonly SingleFlightRefresh _singleFlight;
    private readonly IOptions<MisaESignOptions> _options;
    private readonly ICorrelationIdAccessor _correlation;
    private readonly ISystemClock _clock;

    public RemoteSigningAuthHandler(
        Func<EnsureAccessToken> ensureFactory,
        Func<RefreshAccessToken> refreshFactory,
        ITokenCache cache,
        ITokenCacheKeySelector keySelector,
        SingleFlightRefresh singleFlight,
        IOptions<MisaESignOptions> options,
        ICorrelationIdAccessor correlation,
        ISystemClock clock)
    {
        _ensureFactory = ensureFactory;
        _refreshFactory = refreshFactory;
        _cache = cache;
        _keySelector = keySelector;
        _singleFlight = singleFlight;
        _options = options;
        _correlation = AmbientCorrelationId.Wrap(correlation);
        _clock = clock;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (IsAuthEndpoint(request))
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        // Auth calls made below run in this handler's DI scope; keep them under the flow's ID.
        using var correlationScope = AmbientCorrelationId.PushFrom(request);

        // Always use the current token: callers capture a token once and pass it to every
        // wire call, so the header they set can be stale after a refresh.
        var current = await _ensureFactory().ExecuteAsync(cancellationToken).ConfigureAwait(false);
        ApplyAuth(request, current.Value);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }
        response.Dispose();

        var cacheKey = _keySelector.Compose();
        var token = await RecoverTokenAsync(cacheKey, current, cancellationToken).ConfigureAwait(false);

        var retry = CloneForRetry(request);
        ApplyAuth(retry, token.Value);
        var retryResponse = await base.SendAsync(retry, cancellationToken).ConfigureAwait(false);
        if (retryResponse.StatusCode == HttpStatusCode.Unauthorized)
        {
            retryResponse.Dispose();
            await EvictIfHoldsAsync(cacheKey, token.Value, cancellationToken).ConfigureAwait(false);
            throw new AuthenticationFailedException(
                rawCode: "401",
                detail: "MISA eSign rejected the request with 401 even after a fresh access token.",
                correlationId: _correlation.Current);
        }
        return retryResponse;
    }

    private async Task<AccessToken> RecoverTokenAsync(string cacheKey, AccessToken sent, CancellationToken ct)
    {
        try
        {
            var refreshed = await _singleFlight.RefreshAsync(
                cacheKey,
                async innerCt =>
                {
                    // Another flow may already have replaced the token we sent; reuse it.
                    var cached = await _cache.TryGetAsync(cacheKey, innerCt).ConfigureAwait(false);
                    if (cached is not null && !string.Equals(cached.Value, sent.Value, StringComparison.Ordinal))
                    {
                        return cached;
                    }
                    if (cached is null || string.IsNullOrEmpty(cached.RefreshToken))
                    {
                        throw new InvalidOperationException("No cached refresh token is available.");
                    }
                    var result = await _refreshFactory().ExecuteAsync(cached.RefreshToken, cached.UserId, cached.Username, innerCt).ConfigureAwait(false);
                    if (result.ExpiresAtUtc <= _clock.UtcNow)
                    {
                        throw new InvalidOperationException("Refresh returned an already-expired token.");
                    }
                    await _cache.SetAsync(cacheKey, result, innerCt).ConfigureAwait(false);
                    return result;
                },
                ct).ConfigureAwait(false);
            return refreshed;
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
        {
            // Refresh unavailable or failed. Never evict a token another flow stored meanwhile:
            // reuse it if it is fresh, drop the entry only while it still holds the token we
            // sent, then let EnsureAccessToken log in. Login errors (including 2FA-required)
            // propagate unchanged.
            var current = await _cache.TryGetAsync(cacheKey, ct).ConfigureAwait(false);
            if (current is not null
                && !string.Equals(current.Value, sent.Value, StringComparison.Ordinal)
                && current.ExpiresAtUtc - EnsureAccessToken.ProactiveRefreshSkew > _clock.UtcNow)
            {
                return current;
            }
            await EvictIfHoldsAsync(cacheKey, sent.Value, ct).ConfigureAwait(false);
            return await _ensureFactory().ExecuteAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>Removes the cache entry only while it still holds <paramref name="value"/>.</summary>
    private Task EvictIfHoldsAsync(string cacheKey, string value, CancellationToken ct) =>
        _cache.RemoveIfMatchesAsync(cacheKey, value, ct);

    private static bool IsAuthEndpoint(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        return path.EndsWith("/login-api", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/refreshtoken", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/two-factor-auth", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/resend-otp-auth", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyAuth(HttpRequestMessage request, string accessToken)
    {
        request.Headers.Remove(MisaESignWireClient.AuthHeader);
        request.Headers.TryAddWithoutValidation(MisaESignWireClient.AuthHeader, $"Bearer {accessToken}");
    }

    private static HttpRequestMessage CloneForRetry(HttpRequestMessage source)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri)
        {
            Version = source.Version,
            VersionPolicy = source.VersionPolicy,
            Content = source.Content,
        };
        foreach (var header in source.Headers)
        {
            if (!string.Equals(header.Key, MisaESignWireClient.AuthHeader, StringComparison.OrdinalIgnoreCase))
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
        return clone;
    }
}

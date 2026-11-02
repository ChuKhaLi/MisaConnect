using System.Net;
using Microsoft.Extensions.Options;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Infrastructure.Configuration;
using MisaConnect.ESign.Infrastructure.ESign;

namespace MisaConnect.ESign.Infrastructure.Http;

/// <summary>
/// On every request to a non-auth MISA endpoint: ensure an
/// <see cref="AuthorizationRM"/> header is present (sourced from the cache via
/// <see cref="EnsureAccessToken"/>). On 401: invalidate the cache entry, call
/// <see cref="SingleFlightRefresh"/> to obtain a fresh token, retry the
/// original request exactly once.
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

    public RemoteSigningAuthHandler(
        Func<EnsureAccessToken> ensureFactory,
        Func<RefreshAccessToken> refreshFactory,
        ITokenCache cache,
        ITokenCacheKeySelector keySelector,
        SingleFlightRefresh singleFlight,
        IOptions<MisaESignOptions> options,
        ICorrelationIdAccessor correlation)
    {
        _ensureFactory = ensureFactory;
        _refreshFactory = refreshFactory;
        _cache = cache;
        _keySelector = keySelector;
        _singleFlight = singleFlight;
        _options = options;
        _correlation = correlation;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (IsAuthEndpoint(request))
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        if (!request.Headers.Contains(MisaESignWireClient.AuthHeader))
        {
            var token = await _ensureFactory().ExecuteAsync(cancellationToken).ConfigureAwait(false);
            ApplyAuth(request, token.Value);
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }
        response.Dispose();

        var cacheKey = _keySelector.Compose();
        var existing = await _cache.TryGetAsync(cacheKey, cancellationToken).ConfigureAwait(false);
        if (existing is null || string.IsNullOrEmpty(existing.RefreshToken))
        {
            throw new AuthenticationFailedException(
                rawCode: "401",
                detail: "MISA eSign rejected the request with 401 and no cached refresh token is available.",
                correlationId: _correlation.Current);
        }

        AccessToken refreshed;
        try
        {
            refreshed = await _singleFlight.RefreshAsync(
                cacheKey,
                async ct => await _refreshFactory().ExecuteAsync(existing.RefreshToken, existing.UserId, existing.Username, ct).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);
        }
        catch (ESignException)
        {
            await _cache.RemoveAsync(cacheKey, cancellationToken).ConfigureAwait(false);
            throw;
        }

        await _cache.SetAsync(cacheKey, refreshed, cancellationToken).ConfigureAwait(false);

        var retry = CloneForRetry(request);
        ApplyAuth(retry, refreshed.Value);
        var retryResponse = await base.SendAsync(retry, cancellationToken).ConfigureAwait(false);
        if (retryResponse.StatusCode == HttpStatusCode.Unauthorized)
        {
            retryResponse.Dispose();
            throw new AuthenticationFailedException(
                rawCode: "401",
                detail: "MISA eSign rejected the request with 401 even after a fresh access token.",
                correlationId: _correlation.Current);
        }
        return retryResponse;
    }

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

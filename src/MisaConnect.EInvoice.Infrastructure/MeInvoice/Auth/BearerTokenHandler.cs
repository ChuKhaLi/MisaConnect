using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Infrastructure.Configuration;

namespace MisaConnect.EInvoice.Infrastructure.MeInvoice.Auth;

public sealed class BearerTokenHandler : DelegatingHandler
{
    private readonly Func<EnsureAccessToken> EnsureTokenFactory;
    private readonly ITokenCache _cache;
    private readonly IOptions<MisaEInvoiceOptions> _options;

    public BearerTokenHandler(Func<EnsureAccessToken> ensureTokenFactory, ITokenCache cache, IOptions<MisaEInvoiceOptions> options)
    {
        EnsureTokenFactory = ensureTokenFactory;
        _cache = cache;
        _options = options;
    }

    private EnsureAccessToken EnsureToken => EnsureTokenFactory();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (IsTokenEndpoint(request))
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var wireTaxCode = WireTaxCode.Compose(_options.Value);

        var token = await EnsureToken.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        ApplyHeaders(request, token, wireTaxCode);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        await _cache.RemoveAsync(wireTaxCode, cancellationToken).ConfigureAwait(false);
        var refreshed = await EnsureToken.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        var retry = CloneForRetry(request);
        ApplyHeaders(retry, refreshed, wireTaxCode);
        var retryResponse = await base.SendAsync(retry, cancellationToken).ConfigureAwait(false);
        if (retryResponse.StatusCode == HttpStatusCode.Unauthorized)
        {
            retryResponse.Dispose();
            throw new MeInvoiceException(MeInvoiceErrorCategory.Authentication, "UnAuthorize", "MISA rejected the access token even after refresh.");
        }
        return retryResponse;
    }

    private static bool IsTokenEndpoint(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        return path.EndsWith("/webapp/token", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyHeaders(HttpRequestMessage request, AccessToken token, string wireTaxCode)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        request.Headers.Remove("taxcode");
        request.Headers.TryAddWithoutValidation("taxcode", wireTaxCode);
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
            if (!string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(header.Key, "taxcode", StringComparison.OrdinalIgnoreCase))
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
        return clone;
    }
}

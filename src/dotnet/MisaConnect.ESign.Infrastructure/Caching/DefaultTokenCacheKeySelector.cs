using Microsoft.Extensions.Options;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Infrastructure.Configuration;

namespace MisaConnect.ESign.Infrastructure.Caching;

internal sealed class DefaultTokenCacheKeySelector : ITokenCacheKeySelector
{
    private readonly IOptions<MisaESignOptions> _options;
    private readonly IMisaCredentialsAccessor _credentials;

    public DefaultTokenCacheKeySelector(IOptions<MisaESignOptions> options, IMisaCredentialsAccessor credentials)
    {
        _options = options;
        _credentials = credentials;
    }

    public string Compose()
    {
        // Identity comes from the per-call credentials accessor so two signers
        // never share a cache slot. The host is global (BaseUrl).
        var creds = _credentials.Get();
        if (string.IsNullOrEmpty(creds.UserName) || string.IsNullOrEmpty(creds.ClientId))
        {
            // Fail loudly instead of collapsing to "||host" and bleeding tokens
            // across identities (the exact failure this seam exists to prevent).
            // A missing-ambient bug (consumer forgot to set the AsyncLocal, or
            // blank static options under a misconfig) surfaces here at the call
            // site rather than silently in the shared cache.
            throw new InvalidOperationException(
                "Cannot compose the token-cache key: the resolved MISA UserName and ClientId must both be non-empty. " +
                "In Dynamic credentials mode, ensure the per-call credentials ambient is set around the SDK call.");
        }

        var host = Uri.TryCreate(_options.Value.BaseUrl, UriKind.Absolute, out var uri) ? uri.Host : _options.Value.BaseUrl;
        return $"{creds.UserName}|{creds.ClientId}|{host}";
    }
}

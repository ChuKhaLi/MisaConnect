using Microsoft.Extensions.Options;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Infrastructure.Configuration;

namespace MisaConnect.ESign.Infrastructure.Caching;

internal sealed class DefaultTokenCacheKeySelector : ITokenCacheKeySelector
{
    private readonly IOptions<MisaESignOptions> _options;

    public DefaultTokenCacheKeySelector(IOptions<MisaESignOptions> options)
    {
        _options = options;
    }

    public string Compose()
    {
        var o = _options.Value;
        var host = Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out var uri) ? uri.Host : o.BaseUrl;
        return $"{o.UserName}|{o.ClientId}|{host}";
    }
}

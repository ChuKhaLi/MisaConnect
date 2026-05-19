using Microsoft.Extensions.Options;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Infrastructure.Configuration;
using MisaConnect.ESign.Infrastructure.ESign;

namespace MisaConnect.ESign.Infrastructure.Http;

/// <summary>
/// Injects <c>x-clientId</c>, <c>x-clientKey</c>, and <c>X-Correlation-Id</c>
/// on every outbound MISA eSign request. The <c>AuthorizationRM</c> header is
/// added by <see cref="MisaESignWireClient"/> (login/refresh skip it) or by
/// <see cref="RemoteSigningAuthHandler"/> for downstream calls.
/// </summary>
internal sealed class ClientHeadersHandler : DelegatingHandler
{
    private readonly IOptions<MisaESignOptions> _options;
    private readonly ICorrelationIdAccessor _correlation;

    public ClientHeadersHandler(IOptions<MisaESignOptions> options, ICorrelationIdAccessor correlation)
    {
        _options = options;
        _correlation = correlation;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!request.Headers.Contains(MisaESignWireClient.ClientIdHeader))
        {
            request.Headers.TryAddWithoutValidation(MisaESignWireClient.ClientIdHeader, _options.Value.ClientId);
        }
        if (!request.Headers.Contains(MisaESignWireClient.ClientKeyHeader))
        {
            request.Headers.TryAddWithoutValidation(MisaESignWireClient.ClientKeyHeader, _options.Value.ClientKey);
        }
        if (!request.Headers.Contains(MisaESignWireClient.CorrelationIdHeader))
        {
            request.Headers.TryAddWithoutValidation(MisaESignWireClient.CorrelationIdHeader, _correlation.Current);
        }
        return base.SendAsync(request, cancellationToken);
    }
}

using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Infrastructure.Credentials;
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
    private readonly EffectiveCredentials _credentials;
    private readonly ICorrelationIdAccessor _correlation;

    public ClientHeadersHandler(EffectiveCredentials credentials, ICorrelationIdAccessor correlation)
    {
        _credentials = credentials;
        _correlation = AmbientCorrelationId.Wrap(correlation);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Resolve credentials per-call so a consumer's per-call ambient flows in.
        // The Contains guards remain: MisaESignWireClient.NewRequest pre-stamps
        // these from the same accessor, so the values are identical and the guard
        // is harmless; it also lets any future pre-stamped header win.
        if (!request.Headers.Contains(MisaESignWireClient.ClientIdHeader)
            || !request.Headers.Contains(MisaESignWireClient.ClientKeyHeader))
        {
            var creds = _credentials.Get();
            if (!request.Headers.Contains(MisaESignWireClient.ClientIdHeader))
            {
                request.Headers.TryAddWithoutValidation(MisaESignWireClient.ClientIdHeader, creds.ClientId);
            }
            if (!request.Headers.Contains(MisaESignWireClient.ClientKeyHeader))
            {
                request.Headers.TryAddWithoutValidation(MisaESignWireClient.ClientKeyHeader, creds.ClientKey);
            }
        }
        if (!request.Headers.Contains(MisaESignWireClient.CorrelationIdHeader))
        {
            request.Headers.TryAddWithoutValidation(MisaESignWireClient.CorrelationIdHeader, _correlation.Current);
        }
        return base.SendAsync(request, cancellationToken);
    }
}

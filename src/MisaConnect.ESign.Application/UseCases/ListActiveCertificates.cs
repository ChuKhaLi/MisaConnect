using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Domain.Certificates;
using MisaConnect.ESign.Domain.Errors;

namespace MisaConnect.ESign.Application.UseCases;

public sealed class ListActiveCertificates
{
    private readonly IMisaESignWireClient _wire;
    private readonly ICorrelationIdAccessor _correlation;

    public ListActiveCertificates(IMisaESignWireClient wire, ICorrelationIdAccessor correlation)
    {
        _wire = wire;
        _correlation = correlation;
    }

    public async Task<IReadOnlyList<Certificate>> ExecuteAsync(string accessToken, CancellationToken ct)
    {
        var all = await _wire.ListCertificatesByUserIdAsync(accessToken, ct).ConfigureAwait(false);
        var active = new List<Certificate>(all.Count);
        foreach (var c in all)
        {
            if (c.KeyStatus == KeyStatus.ACTIVE)
            {
                active.Add(c);
            }
        }
        if (active.Count == 0)
        {
            throw new NoActiveCertificateException(
                "No certificate with keyStatus = ACTIVE is available for the authenticated user.",
                _correlation.Current);
        }
        return active;
    }
}

using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Domain.Certificates;

namespace MisaConnect.ESign.Infrastructure.Certificates;

internal sealed class FirstActiveCertificateSelector : ICertificateSelector
{
    public Task<Certificate> SelectAsync(IReadOnlyList<Certificate> activeCertificates, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(activeCertificates);
        if (activeCertificates.Count == 0)
        {
            throw new ArgumentException("activeCertificates must be non-empty.", nameof(activeCertificates));
        }
        return Task.FromResult(activeCertificates[0]);
    }
}

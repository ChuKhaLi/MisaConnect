using MisaConnect.ESign.Domain.Certificates;

namespace MisaConnect.ESign.Application.Abstractions;

public interface ICertificateSelector
{
    Task<Certificate> SelectAsync(IReadOnlyList<Certificate> activeCertificates, CancellationToken ct);
}

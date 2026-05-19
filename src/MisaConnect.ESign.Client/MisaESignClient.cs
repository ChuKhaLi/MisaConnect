using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Client.Dtos;
using MisaConnect.ESign.Client.Mapping;

namespace MisaConnect.ESign.Client;

internal sealed class MisaESignClient : IMisaESignClient
{
    private readonly SignPdf _orchestrator;

    public MisaESignClient(SignPdf orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public async Task<SignPdfResultDto> SignPdfAsync(SignPdfRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var workRequest = SignPdfRequestMapper.ToWorkRequest(request);
        var result = await _orchestrator.ExecuteAsync(workRequest, ct).ConfigureAwait(false);
        return new SignPdfResultDto(
            SignedPdf: result.SignedPdf.Bytes,
            TransactionId: result.TransactionId,
            CertificateKeyAlias: result.CertificateKeyAlias,
            CompletedAtUtc: result.CompletedAtUtc);
    }
}

using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Domain.Certificates;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Signing;

namespace MisaConnect.ESign.Application.UseCases;

public sealed class HashPdfDocument
{
    private readonly IMisaESignWireClient _wire;
    private readonly ICorrelationIdAccessor _correlation;

    public HashPdfDocument(IMisaESignWireClient wire, ICorrelationIdAccessor correlation)
    {
        _wire = wire;
        _correlation = correlation;
    }

    public async Task<PdfHashOutput> ExecuteAsync(
        string accessToken,
        Certificate cert,
        byte[] pdfBytes,
        string documentId,
        SignatureInfo signatureInfo,
        CancellationToken ct)
    {
        var output = await _wire.HashPdfAsync(accessToken, cert, pdfBytes, documentId, signatureInfo, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(output.Digest))
        {
            throw new ESignGeneralException(
                ESignErrorCategory.HashRejected,
                "EmptyDigest",
                "MISA documents/hash returned an empty digest.",
                _correlation.Current);
        }
        return output;
    }
}

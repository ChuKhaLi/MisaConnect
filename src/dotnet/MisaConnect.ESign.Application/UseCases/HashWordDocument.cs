using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Domain.Certificates;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Signing;

namespace MisaConnect.ESign.Application.UseCases;

public sealed class HashWordDocument
{
    private readonly IMisaESignWireClient _wire;
    private readonly ICorrelationIdAccessor _correlation;

    public HashWordDocument(IMisaESignWireClient wire, ICorrelationIdAccessor correlation)
    {
        _wire = wire;
        _correlation = correlation;
    }

    public async Task<WordExcelHashOutput> ExecuteAsync(
        string accessToken,
        Certificate cert,
        byte[] wordBytes,
        string documentId,
        SignatureInfo signatureInfo,
        CancellationToken ct)
    {
        var output = await _wire.HashWordAsync(accessToken, cert, wordBytes, documentId, signatureInfo, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(output.DocumentBytes) ||
            string.IsNullOrEmpty(output.SignatureId) ||
            string.IsNullOrEmpty(output.Digest) ||
            string.IsNullOrEmpty(output.MainDom))
        {
            throw new ESignGeneralException(
                ESignErrorCategory.HashRejected,
                "IncompleteHashResponse",
                "MISA documents/hash returned an incomplete word hash response (missing documentBytes, signatureId, digest, or mainDom).",
                _correlation.Current,
                format: DocumentFormat.Word);
        }
        return output;
    }
}

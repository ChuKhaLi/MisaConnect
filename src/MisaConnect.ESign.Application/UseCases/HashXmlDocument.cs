using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Domain.Certificates;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Signing;

namespace MisaConnect.ESign.Application.UseCases;

public sealed class HashXmlDocument
{
    private readonly IMisaESignWireClient _wire;
    private readonly ICorrelationIdAccessor _correlation;

    public HashXmlDocument(IMisaESignWireClient wire, ICorrelationIdAccessor correlation)
    {
        _wire = wire;
        _correlation = correlation;
    }

    public async Task<XmlHashOutput> ExecuteAsync(
        string accessToken,
        Certificate cert,
        string xmlContent,
        string documentId,
        XmlSignatureContext signatureContext,
        CancellationToken ct)
    {
        var output = await _wire.HashXmlAsync(accessToken, cert, xmlContent, documentId, signatureContext, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(output.Document) ||
            string.IsNullOrEmpty(output.SignatureId) ||
            string.IsNullOrEmpty(output.Digest) ||
            string.IsNullOrEmpty(output.Sh))
        {
            throw new ESignGeneralException(
                ESignErrorCategory.HashRejected,
                "IncompleteHashResponse",
                "MISA documents/hash returned an incomplete xml hash response (missing document, signatureId, digest, or sh).",
                _correlation.Current,
                format: DocumentFormat.Xml);
        }
        return output;
    }
}

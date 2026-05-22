using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Domain.Certificates;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;

namespace MisaConnect.ESign.Application.UseCases;

public sealed class AttachSignatureToXml
{
    private readonly IMisaESignWireClient _wire;
    private readonly ICorrelationIdAccessor _correlation;

    public AttachSignatureToXml(IMisaESignWireClient wire, ICorrelationIdAccessor correlation)
    {
        _wire = wire;
        _correlation = correlation;
    }

    public async Task<byte[]> ExecuteAsync(
        string accessToken,
        Certificate cert,
        XmlHashOutput hash,
        string signatureData,
        CancellationToken ct)
    {
        var bytes = await _wire.AttachSignatureToXmlAsync(accessToken, cert, hash, signatureData, ct).ConfigureAwait(false);
        if (bytes is null || bytes.Length == 0)
        {
            throw new ESignGeneralException(
                ESignErrorCategory.AttachmentRejected,
                "MissingSignedDocument",
                "MISA documents/attachment returned no signed xml bytes.",
                _correlation.Current,
                format: DocumentFormat.Xml);
        }
        return bytes;
    }
}

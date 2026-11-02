using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Domain.Certificates;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;

namespace MisaConnect.ESign.Application.UseCases;

public sealed class AttachSignatureToWordExcel
{
    private readonly IMisaESignWireClient _wire;
    private readonly ICorrelationIdAccessor _correlation;

    public AttachSignatureToWordExcel(IMisaESignWireClient wire, ICorrelationIdAccessor correlation)
    {
        _wire = wire;
        _correlation = correlation;
    }

    public async Task<byte[]> ExecuteAsync(
        string accessToken,
        Certificate cert,
        WordExcelHashOutput hash,
        string signatureData,
        DocumentFormat format,
        CancellationToken ct)
    {
        if (format != DocumentFormat.Word && format != DocumentFormat.Excel)
        {
            throw new ArgumentException(
                $"AttachSignatureToWordExcel.ExecuteAsync requires format Word or Excel, got {format}.",
                nameof(format));
        }

        var bytes = await _wire.AttachSignatureToWordExcelAsync(accessToken, cert, hash, signatureData, format, ct).ConfigureAwait(false);
        if (bytes is null || bytes.Length == 0)
        {
            throw new ESignGeneralException(
                ESignErrorCategory.AttachmentRejected,
                "MissingSignedDocument",
                $"MISA documents/attachment returned no signed {format} bytes.",
                _correlation.Current,
                format: format);
        }
        return bytes;
    }
}

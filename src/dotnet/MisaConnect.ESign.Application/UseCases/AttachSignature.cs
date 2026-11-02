using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Domain.Certificates;

namespace MisaConnect.ESign.Application.UseCases;

public sealed class AttachSignature
{
    private readonly IMisaESignWireClient _wire;

    public AttachSignature(IMisaESignWireClient wire)
    {
        _wire = wire;
    }

    public Task<byte[]> ExecuteAsync(
        string accessToken,
        Certificate cert,
        PdfHashOutput hash,
        string signatureData,
        CancellationToken ct) =>
        _wire.AttachSignatureAsync(accessToken, cert, hash, signatureData, ct);
}

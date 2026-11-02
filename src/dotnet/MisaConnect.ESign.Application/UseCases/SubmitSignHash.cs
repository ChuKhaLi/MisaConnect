using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Domain.Certificates;
using MisaConnect.ESign.Domain.Signing;

namespace MisaConnect.ESign.Application.UseCases;

public sealed class SubmitSignHash
{
    private readonly IMisaESignWireClient _wire;

    public SubmitSignHash(IMisaESignWireClient wire)
    {
        _wire = wire;
    }

    public Task<SignTransaction> ExecuteAsync(
        string accessToken,
        Certificate cert,
        string userId,
        string dataToBeDisplayed,
        SignHashInput hash,
        string documentName,
        CancellationToken ct) =>
        _wire.SubmitSignHashAsync(accessToken, cert, userId, dataToBeDisplayed, hash, documentName, ct);
}

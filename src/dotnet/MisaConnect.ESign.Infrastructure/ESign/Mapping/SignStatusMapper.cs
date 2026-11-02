using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Domain.Signing;
using MisaConnect.ESign.Infrastructure.ESign.Wire;

namespace MisaConnect.ESign.Infrastructure.ESign.Mapping;

internal static class SignStatusMapper
{
    public static SignStatusSnapshot ToSnapshot(SignStatusResponseDto dto, string transactionId)
    {
        var status = ParseStatus(dto.Status);
        var firstSig = dto.Signatures is { Count: > 0 } ? dto.Signatures[0].Signature : null;
        return new SignStatusSnapshot(
            Status: status,
            ErrorCode: dto.ErrorCode,
            ErrorDescription: dto.ErrorDescription,
            TransactionId: dto.TransactionId ?? transactionId,
            FirstSignatureData: firstSig);
    }

    public static SignStatus ParseStatus(string? raw) => raw?.ToUpperInvariant() switch
    {
        "PENDING" => SignStatus.PENDING,
        "SUCCESS" => SignStatus.SUCCESS,
        "FAILED" => SignStatus.FAILED,
        "CANCELLED" or "CANCELED" => SignStatus.CANCELLED,
        _ => SignStatus.UNKNOWN,
    };
}

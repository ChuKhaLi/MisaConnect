using MisaConnect.ESign.Domain.Signing;

namespace MisaConnect.ESign.Application.Abstractions;

public sealed record SignStatusSnapshot(
    SignStatus Status,
    string? ErrorCode,
    string? ErrorDescription,
    string TransactionId,
    string? FirstSignatureData);

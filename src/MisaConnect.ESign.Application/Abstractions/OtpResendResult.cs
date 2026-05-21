namespace MisaConnect.ESign.Application.Abstractions;

public sealed record OtpResendResult(
    bool Success,
    string? RawCode,
    string? UserMsg,
    string? DevMsg,
    string CorrelationId);

namespace MisaConnect.ESign.Client.Dtos;

public sealed record OtpResendResultDto(
    bool Success,
    string? RawCode,
    string? UserMsg,
    string? DevMsg,
    string CorrelationId);

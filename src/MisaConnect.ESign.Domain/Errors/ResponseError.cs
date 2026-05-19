namespace MisaConnect.ESign.Domain.Errors;

public sealed record ResponseError(
    bool Error,
    string? ErrorCode,
    string? DevMsg,
    string? UserMsg);

namespace MisaConnect.ESign.Domain.Errors;

public sealed class ExhaustedOtpAttemptsException : AuthenticationFailedException
{
    public ExhaustedOtpAttemptsException(
        string? rawCode,
        string detail,
        string correlationId,
        Exception? inner = null)
        : base(rawCode ?? "ExhaustedOtpAttempts", detail, correlationId, requires2FA: false, username: string.Empty, inner)
    {
    }
}

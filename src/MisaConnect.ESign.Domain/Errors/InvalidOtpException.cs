namespace MisaConnect.ESign.Domain.Errors;

public sealed class InvalidOtpException : AuthenticationFailedException
{
    public InvalidOtpException(
        string? rawCode,
        string detail,
        string correlationId,
        Exception? inner = null)
        : base(rawCode ?? "InvalidOtp", detail, correlationId, requires2FA: false, username: string.Empty, inner)
    {
    }
}

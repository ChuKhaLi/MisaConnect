namespace MisaConnect.ESign.Domain.Errors;

public sealed class ExpiredOtpException : AuthenticationFailedException
{
    public ExpiredOtpException(
        string? rawCode,
        string detail,
        string correlationId,
        Exception? inner = null)
        : base(rawCode ?? "ExpiredOtp", detail, correlationId, requires2FA: false, username: string.Empty, inner)
    {
    }
}

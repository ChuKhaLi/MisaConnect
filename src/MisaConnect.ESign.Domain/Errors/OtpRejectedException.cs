namespace MisaConnect.ESign.Domain.Errors;

public sealed class OtpRejectedException : AuthenticationFailedException
{
    public OtpRejectedException(
        string? rawCode,
        string detail,
        string correlationId,
        Exception? inner = null)
        : base(rawCode ?? "OtpRejected", detail, correlationId, requires2FA: false, username: string.Empty, inner)
    {
    }
}

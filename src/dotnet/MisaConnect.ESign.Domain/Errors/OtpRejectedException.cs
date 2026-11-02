using MisaConnect.ESign.Domain.Documents;

namespace MisaConnect.ESign.Domain.Errors;

public sealed class OtpRejectedException : AuthenticationFailedException
{
    public OtpRejectedException(
        string? rawCode,
        string detail,
        string correlationId,
        Exception? inner = null,
        DocumentFormat format = DocumentFormat.Unknown)
        : base(rawCode ?? "OtpRejected", detail, correlationId, requires2FA: false, username: string.Empty, inner, format)
    {
    }
}

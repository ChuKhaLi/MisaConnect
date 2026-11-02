using MisaConnect.ESign.Domain.Documents;

namespace MisaConnect.ESign.Domain.Errors;

public class AuthenticationFailedException : ESignException
{
    public AuthenticationFailedException(
        string? rawCode,
        string detail,
        string correlationId,
        bool requires2FA = false,
        string username = "",
        Exception? inner = null,
        DocumentFormat format = DocumentFormat.Unknown)
        : base(ESignErrorCategory.Authentication, rawCode, detail, correlationId, inner, format)
    {
        Requires2FA = requires2FA;
        Username = username ?? string.Empty;
    }

    public bool Requires2FA { get; }

    public string Username { get; }
}

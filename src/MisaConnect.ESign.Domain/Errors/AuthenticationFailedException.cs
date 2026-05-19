namespace MisaConnect.ESign.Domain.Errors;

public sealed class AuthenticationFailedException : ESignException
{
    public AuthenticationFailedException(
        string? rawCode,
        string detail,
        string correlationId,
        bool requires2FA = false,
        Exception? inner = null)
        : base(ESignErrorCategory.Authentication, rawCode, detail, correlationId, inner)
    {
        Requires2FA = requires2FA;
    }

    public bool Requires2FA { get; }
}

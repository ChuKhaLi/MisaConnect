namespace MisaConnect.ESign.Domain.Errors;

public sealed class SignRejectedException : ESignException
{
    public SignRejectedException(
        string? rawCode,
        string detail,
        string correlationId,
        bool requiresUserCertSetup = false,
        Exception? inner = null)
        : base(ESignErrorCategory.SignRejected, rawCode, detail, correlationId, inner)
    {
        RequiresUserCertSetup = requiresUserCertSetup;
    }

    public bool RequiresUserCertSetup { get; }
}

using MisaConnect.ESign.Domain.Documents;

namespace MisaConnect.ESign.Domain.Errors;

public sealed class SignRejectedException : ESignException
{
    public SignRejectedException(
        string? rawCode,
        string detail,
        string correlationId,
        bool requiresUserCertSetup = false,
        Exception? inner = null,
        DocumentFormat format = DocumentFormat.Unknown)
        : base(ESignErrorCategory.SignRejected, rawCode, detail, correlationId, inner, format)
    {
        RequiresUserCertSetup = requiresUserCertSetup;
    }

    public bool RequiresUserCertSetup { get; }
}

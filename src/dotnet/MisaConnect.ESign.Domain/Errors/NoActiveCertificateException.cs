using MisaConnect.ESign.Domain.Documents;

namespace MisaConnect.ESign.Domain.Errors;

public sealed class NoActiveCertificateException : ESignException
{
    public NoActiveCertificateException(
        string detail,
        string correlationId,
        Exception? inner = null,
        DocumentFormat format = DocumentFormat.Unknown)
        : base(ESignErrorCategory.NoActiveCertificate, "NoActiveCertificate", detail, correlationId, inner, format)
    {
    }
}

namespace MisaConnect.ESign.Domain.Errors;

public sealed class NoActiveCertificateException : ESignException
{
    public NoActiveCertificateException(string detail, string correlationId, Exception? inner = null)
        : base(ESignErrorCategory.NoActiveCertificate, "NoActiveCertificate", detail, correlationId, inner)
    {
    }
}

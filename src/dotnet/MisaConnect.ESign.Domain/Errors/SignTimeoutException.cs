using MisaConnect.ESign.Domain.Documents;

namespace MisaConnect.ESign.Domain.Errors;

public sealed class SignTimeoutException : ESignException
{
    public SignTimeoutException(
        string transactionId,
        TimeSpan elapsedTime,
        string detail,
        string correlationId,
        Exception? inner = null,
        DocumentFormat format = DocumentFormat.Unknown)
        : base(ESignErrorCategory.SignTimeout, "SignTimeout", detail, correlationId, inner, format)
    {
        TransactionId = transactionId;
        ElapsedTime = elapsedTime;
    }

    public string TransactionId { get; }
    public TimeSpan ElapsedTime { get; }
}

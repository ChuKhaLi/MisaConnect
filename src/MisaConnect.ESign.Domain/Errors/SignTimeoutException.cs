namespace MisaConnect.ESign.Domain.Errors;

public sealed class SignTimeoutException : ESignException
{
    public SignTimeoutException(
        string transactionId,
        TimeSpan elapsedTime,
        string detail,
        string correlationId,
        Exception? inner = null)
        : base(ESignErrorCategory.SignTimeout, "SignTimeout", detail, correlationId, inner)
    {
        TransactionId = transactionId;
        ElapsedTime = elapsedTime;
    }

    public string TransactionId { get; }
    public TimeSpan ElapsedTime { get; }
}

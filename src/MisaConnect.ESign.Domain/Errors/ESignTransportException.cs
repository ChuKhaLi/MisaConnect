using System.Net;

namespace MisaConnect.ESign.Domain.Errors;

public sealed class ESignTransportException : ESignException
{
    public ESignTransportException(
        HttpStatusCode? lastStatusCode,
        int attemptCount,
        string detail,
        string correlationId,
        Exception? inner = null)
        : base(ESignErrorCategory.Transport, lastStatusCode?.ToString() ?? "Transport", detail, correlationId, inner)
    {
        LastStatusCode = lastStatusCode;
        AttemptCount = attemptCount;
    }

    public HttpStatusCode? LastStatusCode { get; }
    public int AttemptCount { get; }
}

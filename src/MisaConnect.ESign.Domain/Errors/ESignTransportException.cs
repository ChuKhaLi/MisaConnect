using System.Net;
using MisaConnect.ESign.Domain.Documents;

namespace MisaConnect.ESign.Domain.Errors;

public sealed class ESignTransportException : ESignException
{
    public ESignTransportException(
        HttpStatusCode? lastStatusCode,
        int attemptCount,
        string detail,
        string correlationId,
        Exception? inner = null,
        DocumentFormat format = DocumentFormat.Unknown)
        : base(ESignErrorCategory.Transport, lastStatusCode?.ToString() ?? "Transport", detail, correlationId, inner, format)
    {
        LastStatusCode = lastStatusCode;
        AttemptCount = attemptCount;
    }

    public HttpStatusCode? LastStatusCode { get; }
    public int AttemptCount { get; }
}

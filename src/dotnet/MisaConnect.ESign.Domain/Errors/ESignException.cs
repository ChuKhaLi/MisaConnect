using MisaConnect.ESign.Domain.Documents;

namespace MisaConnect.ESign.Domain.Errors;

public abstract class ESignException : Exception
{
    protected ESignException(
        ESignErrorCategory category,
        string? rawCode,
        string detail,
        string correlationId,
        Exception? inner = null,
        DocumentFormat format = DocumentFormat.Unknown)
        : base(detail, inner)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw new ArgumentException("CorrelationId must be non-empty.", nameof(correlationId));
        }

        Category = category;
        RawCode = rawCode;
        Detail = detail;
        CorrelationId = correlationId;
        Format = format;
    }

    public ESignErrorCategory Category { get; }
    public string? RawCode { get; }
    public string Detail { get; }
    public string CorrelationId { get; }
    public DocumentFormat Format { get; }
}

public sealed class ESignGeneralException : ESignException
{
    public ESignGeneralException(
        ESignErrorCategory category,
        string? rawCode,
        string detail,
        string correlationId,
        Exception? inner = null,
        DocumentFormat format = DocumentFormat.Unknown)
        : base(category, rawCode, detail, correlationId, inner, format)
    {
    }
}

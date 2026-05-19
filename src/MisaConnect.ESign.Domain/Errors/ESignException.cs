namespace MisaConnect.ESign.Domain.Errors;

public abstract class ESignException : Exception
{
    protected ESignException(
        ESignErrorCategory category,
        string? rawCode,
        string detail,
        string correlationId,
        Exception? inner = null)
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
    }

    public ESignErrorCategory Category { get; }
    public string? RawCode { get; }
    public string Detail { get; }
    public string CorrelationId { get; }
}

public sealed class ESignGeneralException : ESignException
{
    public ESignGeneralException(
        ESignErrorCategory category,
        string? rawCode,
        string detail,
        string correlationId,
        Exception? inner = null)
        : base(category, rawCode, detail, correlationId, inner)
    {
    }
}

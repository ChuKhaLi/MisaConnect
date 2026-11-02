namespace MisaConnect.EInvoice.Domain.Errors;

public sealed class MeInvoiceException : Exception
{
    public MeInvoiceErrorCategory Category { get; }
    public string? RawErrorCode { get; }
    public string? Field { get; }
    public string? Component { get; }
    public string? RefId { get; }
    public IReadOnlyList<ValidationFailure>? Failures { get; }

    public MeInvoiceException(
        MeInvoiceErrorCategory category,
        string? rawErrorCode = null,
        string? message = null,
        string? field = null,
        string? component = null,
        string? refId = null,
        IReadOnlyList<ValidationFailure>? failures = null,
        Exception? inner = null)
        : base(message ?? rawErrorCode ?? category.ToString(), inner)
    {
        Category = category;
        RawErrorCode = rawErrorCode;
        Field = field;
        Component = component;
        RefId = refId;
        Failures = failures;
    }
}

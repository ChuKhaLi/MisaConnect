namespace MisaConnect.EInvoice.Domain.Errors;

public sealed record MeInvoiceErrorCode(
    MeInvoiceErrorCategory Category,
    string RawCode,
    string? Detail = null,
    string? Field = null,
    string? Component = null);

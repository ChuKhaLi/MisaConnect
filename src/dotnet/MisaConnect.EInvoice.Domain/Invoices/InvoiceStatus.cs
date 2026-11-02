namespace MisaConnect.EInvoice.Domain.Invoices;

/// <summary>
/// Unified six-value invoice status surfaced to callers (FR-048). Derived from
/// MISA's two raw axes (<c>EInvoiceStatus</c> + <c>PublishStatus</c>) by the
/// pure two-axis precedence rule in
/// <see cref="MisaConnect.EInvoice.Application.Mapping.InvoiceStatusMapper"/>. The raw
/// values remain on <see cref="MisaConnect.EInvoice.Application.Results.InvoiceSnapshot"/>
/// for callers that need finer granularity.
/// </summary>
public enum InvoiceStatus
{
    Draft,
    Signed,
    Issued,
    Cancelled,
    Replaced,
    Adjusted,
}

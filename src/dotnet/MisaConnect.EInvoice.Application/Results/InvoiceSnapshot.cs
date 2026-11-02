using MisaConnect.EInvoice.Domain.Invoices;

namespace MisaConnect.EInvoice.Application.Results;

/// <summary>
/// Read-only snapshot of a MISA invoice's current state at the moment of the
/// lookup call (FR-049). Carries both MISA's raw status axes
/// (<see cref="RawEInvoiceStatus"/>, <see cref="RawPublishStatus"/>) and the
/// unified <see cref="Status"/> derived per FR-048. Out-of-scope fields
/// (line items, buyer contact details, PII, custom fields, adjustment
/// metadata beyond <see cref="OrgRefID"/>) are omitted per R-LU-09.
/// </summary>
public sealed record InvoiceSnapshot(
    RefId RefId,
    string? InvoiceTemplateID,
    string? InvSeries,
    DateTime? InvDate,
    string? InvNo,
    string? AccountObjectTaxCode,
    string? AccountObjectName,
    decimal? TotalSaleAmount,
    decimal? TotalVATAmount,
    decimal? TotalAmount,
    decimal? TotalSaleAmountOC,
    decimal? TotalVATAmountOC,
    decimal? TotalAmountOC,
    int? RawEInvoiceStatus,
    int? RawPublishStatus,
    InvoiceStatus Status,
    RefId? OrgRefID,
    DateTimeOffset? CreatedDate,
    DateTimeOffset? ModifiedDate);

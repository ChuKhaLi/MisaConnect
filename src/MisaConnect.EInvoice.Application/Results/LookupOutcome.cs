using MisaConnect.EInvoice.Domain.Invoices;

namespace MisaConnect.EInvoice.Application.Results;

public enum LookupStatus
{
    Found,
    NotFound,
    Validation,
    AuthFailed,
    Configuration,
    MisaThrottled,
    MisaUnavailable,
    TransportFailed,
    MisaUnknown,
}

/// <summary>
/// Per-RefID lookup outcome (FR-042, FR-050). Cartesian invariants
/// enforced by <see cref="MisaConnect.EInvoice.Application.UseCases.LookupByRefIds"/>:
/// <see cref="Snapshot"/> is non-null iff <see cref="Status"/> is
/// <see cref="LookupStatus.Found"/>; <see cref="RawErrorCode"/> is non-null
/// for every error category (anything other than Found / NotFound).
/// </summary>
public sealed record LookupOutcome(
    LookupStatus Status,
    InvoiceSnapshot? Snapshot,
    string? RawErrorCode,
    string? Message,
    RefId RefId);

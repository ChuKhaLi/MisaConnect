using MisaConnect.EInvoice.Domain.Invoices;

namespace MisaConnect.EInvoice.Application.Results;

public enum DeleteDraftStatus
{
    Deleted,
    NotFound,
    NotDeletable,
    AuthFailed,
    Configuration,
    MisaThrottled,
    MisaUnavailable,
    TransportFailed,
    MisaUnknown,
}

public sealed record DeleteDraftOutcome(
    DeleteDraftStatus Status,
    string? RawErrorCode,
    string? Message,
    string? Field,
    RefId? RefId);

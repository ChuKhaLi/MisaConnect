using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;

namespace MisaConnect.EInvoice.Application.UseCases;

/// <summary>
/// Slice 6 (FR-057) — caller-shaped request for issuing an adjustment
/// invoice. The <see cref="Invoice"/> carries delta-only line items and
/// totals (positive, negative, or zero-net per spec Clarifications
/// Session 2026-05-13). The service does NOT enforce direction.
/// </summary>
public sealed record AdjustmentRequest(
    Invoice Invoice,
    OriginalInvoiceReference OriginalRef,
    string ChangeReason,
    bool InvoiceWithCode)
{
    public IReadOnlyList<ValidationFailure>? Validate()
    {
        List<ValidationFailure>? failures = null;

        if (OriginalRef is null)
        {
            (failures ??= new()).Add(new ValidationFailure(
                "$originalRef", "Required.", "FR-058"));
        }
        else
        {
            var refFailures = OriginalRef.Validate();
            if (refFailures is { Count: > 0 })
            {
                (failures ??= new()).AddRange(refFailures);
            }
        }

        if (string.IsNullOrWhiteSpace(ChangeReason))
        {
            (failures ??= new()).Add(new ValidationFailure(
                "$changeReason", "Required.", "FR-060"));
        }

        if (Invoice is null)
        {
            (failures ??= new()).Add(new ValidationFailure(
                "$invoice", "Required.", "FR-061"));
        }

        return failures;
    }
}

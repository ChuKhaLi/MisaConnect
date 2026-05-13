using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;

namespace MisaConnect.EInvoice.Application.UseCases;

/// <summary>
/// Slice 6 (FR-056) — caller-shaped request for issuing a replacement
/// invoice. The <see cref="Invoice"/> carries the corrected absolute totals
/// (full reissue semantics, not delta).
/// </summary>
public sealed record ReplacementRequest(
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

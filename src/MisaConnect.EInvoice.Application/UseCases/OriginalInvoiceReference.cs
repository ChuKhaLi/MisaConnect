using MisaConnect.EInvoice.Domain.Errors;

namespace MisaConnect.EInvoice.Application.UseCases;

/// <summary>
/// Slice 6 (FR-058 / FR-059) — the five-field reference block that identifies
/// the original invoice an amendment supersedes (replacement) or corrects
/// (adjustment). All five fields are mandatory; <see cref="Validate"/>
/// produces one <see cref="ValidationFailure"/> per missing field with the
/// rule IDs documented in <c>data-model.md</c>.
/// </summary>
public sealed record OriginalInvoiceReference(
    string OrgRefID,
    string OrgInvNo,
    string OrgInvTemplateNo,
    string OrgInvSeries,
    DateOnly OrgInvDate)
{
    public IReadOnlyList<ValidationFailure>? Validate()
    {
        List<ValidationFailure>? failures = null;

        if (string.IsNullOrWhiteSpace(OrgRefID))
        {
            (failures ??= new()).Add(new ValidationFailure(
                "$originalRef.OrgRefID", "Required.", "FR-058"));
        }

        if (string.IsNullOrWhiteSpace(OrgInvNo))
        {
            (failures ??= new()).Add(new ValidationFailure(
                "$originalRef.OrgInvNo", "Required.", "FR-059"));
        }

        if (string.IsNullOrWhiteSpace(OrgInvTemplateNo))
        {
            (failures ??= new()).Add(new ValidationFailure(
                "$originalRef.OrgInvTemplateNo", "Required.", "FR-059"));
        }

        if (string.IsNullOrWhiteSpace(OrgInvSeries))
        {
            (failures ??= new()).Add(new ValidationFailure(
                "$originalRef.OrgInvSeries", "Required.", "FR-059"));
        }

        if (OrgInvDate == default)
        {
            (failures ??= new()).Add(new ValidationFailure(
                "$originalRef.OrgInvDate", "Required.", "FR-059"));
        }

        return failures;
    }
}

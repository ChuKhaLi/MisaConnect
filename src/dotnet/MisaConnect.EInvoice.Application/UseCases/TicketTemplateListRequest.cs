using MisaConnect.EInvoice.Domain.Errors;

namespace MisaConnect.EInvoice.Application.UseCases;

/// <summary>
/// Query for <see cref="ListTicketTemplates"/>. Maps to MISA's query parameters
/// <c>invoiceWithCode</c>, <c>start</c>, <c>length</c> (max 100, MISA default 20) and
/// <c>invoiceCalcu</c> (<see cref="FromCashRegister"/>: ticket templates from a cash register, MTT).
/// </summary>
public sealed record TicketTemplateListRequest(
    bool InvoiceWithCode = true,
    int Start = 0,
    int Length = 20,
    bool FromCashRegister = false)
{
    public ValidationFailure? Validate()
    {
        if (Start < 0)
        {
            return new ValidationFailure("Start", "Start must be >= 0.", "FR-047");
        }

        if (Length < 1 || Length > 100)
        {
            return new ValidationFailure("Length", "Length must be in [1, 100].", "FR-047");
        }

        return null;
    }
}

using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;

namespace MisaConnect.EInvoice.Application.Abstractions;

public interface IInvoiceValidator
{
    IReadOnlyList<ValidationFailure> Validate(Invoice invoice);
    IReadOnlyList<ValidationFailure> ValidateBatch(BatchSubmission batch);
}

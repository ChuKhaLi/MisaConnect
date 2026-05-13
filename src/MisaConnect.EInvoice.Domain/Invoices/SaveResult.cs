using MisaConnect.EInvoice.Domain.Errors;

namespace MisaConnect.EInvoice.Domain.Invoices;

public enum SaveOutcome { Success, Error }

public sealed record SaveResult(
    RefId RefId,
    SaveOutcome Outcome,
    MeInvoiceErrorCode? Error = null,
    IReadOnlyList<ValidationFailure>? LocalFailures = null);

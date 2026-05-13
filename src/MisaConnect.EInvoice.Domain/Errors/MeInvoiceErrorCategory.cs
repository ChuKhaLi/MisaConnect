namespace MisaConnect.EInvoice.Domain.Errors;

public enum MeInvoiceErrorCategory
{
    Configuration,
    Authentication,
    TemplateState,
    DeclarationState,
    Validation,
    BatchOrSize,
    DuplicateOrUniqueness,
    ResourceNotFound,
    MisaThrottled,
    MisaUnavailable,
    Signing,
    Replacement,
    OtherWorkflow,
    MisaUnknown,
    NotDeletable,
    TransportFailed,
}

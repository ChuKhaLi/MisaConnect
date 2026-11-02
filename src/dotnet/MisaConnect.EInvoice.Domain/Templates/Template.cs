namespace MisaConnect.EInvoice.Domain.Templates;

public sealed record Template(
    string IPTemplateID,
    string InvSeries,
    string TemplateName,
    string? InvTemplateNo,
    int TemplateType,
    bool IsActive,
    bool UsesTaxAuthorityCode,
    bool IsMoreVATRate);

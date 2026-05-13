namespace MisaConnect.EInvoice.Infrastructure.MeInvoice.Wire;

internal sealed record TemplateRequestDto(string TaxCode, string UserName, string Password);

/// <summary>
/// Incoming template from <c>/webapp/templates</c>. MISA uses
/// <c>IPTemplateID</c> on this response even though outgoing
/// <c>/insert</c>/<c>/preview</c> bodies use the different name
/// <c>InvoiceTemplateID</c> — this asymmetry is MISA's, not ours.
/// </summary>
internal sealed record TemplateDto(
    string IPTemplateID,
    string InvSeries,
    string TemplateName,
    string? InvTemplateNo,
    int TemplateType,
    bool Inactive,
    bool IsMoreVATRate);

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

/// <summary>
/// Incoming row from <c>/webapp/templates/ticket</c> (MISA API document §8.2 "Ticket").
/// Field names are MISA's verbatim. Optional fields are nullable: MISA marks
/// <c>ServiceName</c> and <c>IsTicketCode</c> as present in JSON but not in its table.
/// </summary>
internal sealed record TicketTemplateDto(
    string? TicketTemplateID,
    string? TemplateName,
    string? InvSeries,
    bool Inactive,
    [property: System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString)]
    int? TicketType,
    [property: System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString)]
    int? TemplateType,
    string? ServiceName,
    bool? IsInheritFromOldTemplate,
    bool? IsTicketCode);

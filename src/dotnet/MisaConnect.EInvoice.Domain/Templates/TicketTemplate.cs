namespace MisaConnect.EInvoice.Domain.Templates;

/// <summary>
/// An e-ticket template ("mẫu vé") from MISA's <c>/webapp/templates/ticket</c>
/// (API document §3.2, object §8.2). <see cref="IsActive"/> is MISA's <c>Inactive</c> inverted;
/// every other field keeps MISA's meaning. <see cref="TicketType"/> and <see cref="TemplateType"/>
/// are MISA's raw integers (no documented enum).
/// </summary>
public sealed record TicketTemplate(
    string TicketTemplateID,
    string TemplateName,
    string InvSeries,
    bool IsActive,
    int TicketType,
    int TemplateType,
    string? ServiceName,
    bool IsInheritFromOldTemplate,
    bool IsTicketCode);

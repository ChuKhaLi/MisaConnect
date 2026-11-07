using MisaConnect.EInvoice.Application.Results;
using MisaConnect.EInvoice.Client.Dtos;

namespace MisaConnect.EInvoice.Client;

/// <summary>
/// Public surface of the .NET class-library delivery (constitution Principle VI).
/// Mirrors MisaConnect.EInvoice.Api at parity per the operation manifest.
/// </summary>
public interface IMisaEInvoiceClient
{
    Task<IReadOnlyList<TemplateDto>> ListTemplatesAsync(bool withCode = true, CancellationToken ct = default);

    /// <summary>
    /// Lists e-ticket templates (MISA <c>/webapp/templates/ticket</c>), one page at a time. Rows are returned as
    /// MISA sends them, active and inactive (see <see cref="TicketTemplateDto.IsActive"/>). <c>null</c> uses the
    /// defaults of <see cref="TicketTemplateListRequestDto"/>. Throws <c>MeInvoiceException</c> (Validation) for
    /// <c>Start &lt; 0</c> or <c>Length</c> outside 1–100, before any request.
    /// </summary>
    Task<IReadOnlyList<TicketTemplateDto>> ListTicketTemplatesAsync(
        TicketTemplateListRequestDto? request = null,
        CancellationToken ct = default);

    Task<PdfDocumentDto> PreviewAsync(InvoiceDto invoice, bool withCode = true, CancellationToken ct = default);

    Task<IReadOnlyList<SaveResultDto>> SaveDraftAsync(
        IReadOnlyList<InvoiceDto> invoices,
        bool withCode = true,
        CancellationToken ct = default);

    Task<PdfDocumentDto> GetDraftPdfAsync(string refId, bool withCode = true, CancellationToken ct = default);

    Task<DeleteDraftOutcome> DeleteDraftAsync(string refId, bool invoiceWithCode, CancellationToken ct = default);

    // Slice 5 — read-only lookup operations.

    Task<LookupBatchOutcomeDto> LookupByRefIdAsync(
        IReadOnlyList<string> refIds,
        bool invoiceWithCode,
        CancellationToken ct = default);

    Task<PagedLookupResultDto> LookupStandardAsync(
        PagedLookupRequestDto request,
        bool invoiceWithCode,
        CancellationToken ct = default);

    Task<PagedLookupResultDto> LookupCalculatingAsync(
        PagedLookupRequestDto request,
        bool invoiceWithCode,
        CancellationToken ct = default);

    // Slice 6 — amendment operations.

    Task<AmendmentResultDto> IssueReplacementAsync(
        ReplacementRequestDto request,
        CancellationToken ct = default);

    Task<AmendmentResultDto> IssueAdjustmentAsync(
        AdjustmentRequestDto request,
        CancellationToken ct = default);
}

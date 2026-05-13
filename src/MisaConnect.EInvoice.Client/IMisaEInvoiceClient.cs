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

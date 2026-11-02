using MisaConnect.EInvoice.Application.Results;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Pdf;
using MisaConnect.EInvoice.Domain.Templates;

namespace MisaConnect.EInvoice.Application.Abstractions;

/// <summary>
/// Port through which both delivery surfaces (HTTP API and .NET class library)
/// reach the MISA MeInvoice integration. Slice 1 adds the five operations that
/// drive the create-draft flow end-to-end. Slice 5 appends the three read-only
/// lookup operations.
/// </summary>
public interface IMeInvoiceClient
{
    Task<AccessToken> AcquireTokenAsync(CancellationToken ct);

    Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct);

    Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct);

    Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct);

    Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct);

    Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct);

    // Slice 5 — read-only lookup operations. Default interface implementations
    // throw NotImplementedException so existing slice 1/2/4 stubs that don't
    // exercise the lookup path don't need to be retro-fitted; concrete
    // production implementations (MeInvoiceClient, MeInvoiceCallLogger) override
    // these.

    Task<LookupByRefIdChunkResult> LookupByRefIdAsync(
        IReadOnlyList<RefId> refIds,
        bool invoiceWithCode,
        CancellationToken ct)
        => throw new NotImplementedException();

    Task<PagedResult> LookupStandardAsync(
        PagedLookupRequest request,
        bool invoiceWithCode,
        CancellationToken ct)
        => throw new NotImplementedException();

    Task<PagedResult> LookupCalculatingAsync(
        PagedLookupRequest request,
        bool invoiceWithCode,
        CancellationToken ct)
        => throw new NotImplementedException();

    // Slice 6 — amendment operations (replacement / adjustment). Default
    // interface implementations throw NotImplementedException so existing
    // slice 1/2/4/5 test doubles don't need retro-fit; the concrete
    // MeInvoiceClient + MeInvoiceCallLogger override these.

    Task<SaveResult> IssueReplacementAsync(
        Invoice invoice,
        OriginalInvoiceReference originalRef,
        string changeReason,
        bool invoiceWithCode,
        CancellationToken ct)
        => throw new NotImplementedException();

    Task<SaveResult> IssueAdjustmentAsync(
        Invoice invoice,
        OriginalInvoiceReference originalRef,
        string changeReason,
        bool invoiceWithCode,
        CancellationToken ct)
        => throw new NotImplementedException();
}

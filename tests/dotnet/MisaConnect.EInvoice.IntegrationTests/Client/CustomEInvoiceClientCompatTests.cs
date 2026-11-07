using MisaConnect.EInvoice.Application.Results;
using MisaConnect.EInvoice.Client;
using MisaConnect.EInvoice.Client.Dtos;
using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.Client;

/// <summary>
/// A consumer's own <see cref="IMisaEInvoiceClient"/> written against 2.9.0 (no
/// <c>ListTicketTemplatesAsync</c>) must still compile against 2.10.0: the new member has a default
/// implementation. Calling it on such an implementation throws <see cref="NotImplementedException"/>.
/// </summary>
public class CustomEInvoiceClientCompatTests
{
    [Fact]
    public async Task Implementation_written_for_2_9_compiles_and_new_member_throws_NotImplemented()
    {
        IMisaEInvoiceClient client = new ClientWrittenFor29();

        await Assert.ThrowsAsync<NotImplementedException>(() =>
            client.ListTicketTemplatesAsync(ct: TestContext.Current.CancellationToken));
    }

    private sealed class ClientWrittenFor29 : IMisaEInvoiceClient
    {
        public Task<IReadOnlyList<TemplateDto>> ListTemplatesAsync(bool withCode = true, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PdfDocumentDto> PreviewAsync(InvoiceDto invoice, bool withCode = true, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SaveResultDto>> SaveDraftAsync(IReadOnlyList<InvoiceDto> invoices, bool withCode = true, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PdfDocumentDto> GetDraftPdfAsync(string refId, bool withCode = true, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<DeleteDraftOutcome> DeleteDraftAsync(string refId, bool invoiceWithCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<LookupBatchOutcomeDto> LookupByRefIdAsync(IReadOnlyList<string> refIds, bool invoiceWithCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedLookupResultDto> LookupStandardAsync(PagedLookupRequestDto request, bool invoiceWithCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedLookupResultDto> LookupCalculatingAsync(PagedLookupRequestDto request, bool invoiceWithCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AmendmentResultDto> IssueReplacementAsync(ReplacementRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AmendmentResultDto> IssueAdjustmentAsync(AdjustmentRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
    }
}

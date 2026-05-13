using System.Net;
using System.Net.Http.Json;
using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Pdf;
using MisaConnect.EInvoice.Domain.Templates;
using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.Api;

public class PreviewEndpointTests
{
    [Fact]
    public async Task POST_api_invoices_preview_returns_application_pdf()
    {
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 };
        var stub = new StubPreviewClient(pdfBytes);
        await using var factory = new WebFactory(stub);
        using var client = factory.CreateClient();

        var dto = SampleInvoiceFactory.CreateDto();
        using var response = await client.PostAsJsonAsync("/api/invoices/preview", dto);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("inline; filename=\"preview.pdf\"", response.Content.Headers.ContentDisposition?.ToString());
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(pdfBytes, bytes);
    }

    private sealed class StubPreviewClient : IMeInvoiceClient
    {
        private static readonly Template T = new("ipt-1", "1C25MNQ", "Default", null, 1, true, true, false);
        private readonly byte[] _bytes;
        public StubPreviewClient(byte[] bytes) => _bytes = bytes;

        public Task<AccessToken> AcquireTokenAsync(CancellationToken ct) => Task.FromResult(new AccessToken("t", DateTimeOffset.UtcNow.AddDays(1)));
        public Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct) => Task.FromResult<IReadOnlyList<Template>>(new[] { T });
        public Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct) => Task.FromResult(new PdfDocument(_bytes));
        public Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
    }
}

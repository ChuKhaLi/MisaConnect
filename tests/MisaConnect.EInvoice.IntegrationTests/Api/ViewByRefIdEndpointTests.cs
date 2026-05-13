using System.Net;
using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Pdf;
using MisaConnect.EInvoice.Domain.Templates;
using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.Api;

public class ViewByRefIdEndpointTests
{
    [Fact]
    public async Task GET_api_invoices_refId_pdf_returns_application_pdf()
    {
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 };
        var stub = new Stub(pdfBytes);
        await using var factory = new WebFactory(stub);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/invoices/abc/pdf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("invoice-abc.pdf", response.Content.Headers.ContentDisposition?.ToString());
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(pdfBytes, bytes);
    }

    [Fact]
    public async Task GET_api_invoices_unknown_refId_returns_404()
    {
        var stub = new ThrowingStub();
        await using var factory = new WebFactory(stub);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/invoices/xyz/pdf");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ResourceNotFound", body);
    }

    private sealed class Stub : IMeInvoiceClient
    {
        private readonly byte[] _bytes;
        public Stub(byte[] b) => _bytes = b;
        public Task<AccessToken> AcquireTokenAsync(CancellationToken ct) => Task.FromResult(new AccessToken("t", DateTimeOffset.UtcNow.AddDays(1)));
        public Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => Task.FromResult(new PdfDocument(_bytes));
        public Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class ThrowingStub : IMeInvoiceClient
    {
        public Task<AccessToken> AcquireTokenAsync(CancellationToken ct) => Task.FromResult(new AccessToken("t", DateTimeOffset.UtcNow.AddDays(1)));
        public Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) =>
            throw new MeInvoiceException(MeInvoiceErrorCategory.ResourceNotFound, "RefIdNotFound", refId: refId.Value);
        public Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
    }
}

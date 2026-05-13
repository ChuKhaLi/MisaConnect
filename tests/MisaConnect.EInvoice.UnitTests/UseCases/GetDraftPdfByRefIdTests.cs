using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Pdf;
using MisaConnect.EInvoice.Domain.Templates;
using MisaConnect.EInvoice.Infrastructure.Caching;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.UseCases;

public class GetDraftPdfByRefIdTests
{
    [Fact]
    public async Task Empty_RefId_rejected_without_calling_MISA()
    {
        var stub = new Stub();
        var sut = MakeSut(stub);
        var ex = await Assert.ThrowsAsync<MeInvoiceException>(() => sut.ExecuteAsync("", true, default));
        Assert.Equal(MeInvoiceErrorCategory.Validation, ex.Category);
        Assert.Equal(0, stub.Calls);
    }

    [Fact]
    public async Task Whitespace_RefId_rejected()
    {
        var stub = new Stub();
        var sut = MakeSut(stub);
        await Assert.ThrowsAsync<MeInvoiceException>(() => sut.ExecuteAsync("   ", true, default));
    }

    [Fact]
    public async Task ValidPdf_returns_PdfDocument()
    {
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 };
        var stub = new Stub(pdfBytes);
        var sut = MakeSut(stub);
        var result = await sut.ExecuteAsync("abc", true, default);
        Assert.Equal(pdfBytes, result.Content);
    }

    [Fact]
    public async Task NonPdf_bytes_surfaced_as_MisaUnknown()
    {
        var nonPdf = System.Text.Encoding.ASCII.GetBytes("ABC");
        var stub = new Stub(nonPdf);
        var sut = MakeSut(stub);
        var ex = await Assert.ThrowsAsync<MeInvoiceException>(() => sut.ExecuteAsync("abc", true, default));
        Assert.Equal(MeInvoiceErrorCategory.MisaUnknown, ex.Category);
    }

    private static GetDraftPdfByRefId MakeSut(Stub stub)
    {
        var ensure = new EnsureAccessToken(stub, new InMemoryTokenCache(), new SystemClock(TimeProvider.System), () => "key");
        return new GetDraftPdfByRefId(stub, ensure);
    }

    private sealed class Stub : IMeInvoiceClient
    {
        private readonly byte[] _bytes;
        public int Calls { get; private set; }

        public Stub() : this(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }) { }
        public Stub(byte[] bytes) => _bytes = bytes;

        public Task<AccessToken> AcquireTokenAsync(CancellationToken ct) => Task.FromResult(new AccessToken("t", DateTimeOffset.UtcNow.AddDays(1)));
        public Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new PdfDocument(_bytes));
        }
        public Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
    }
}

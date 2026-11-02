using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Application.Validation;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Pdf;
using MisaConnect.EInvoice.Domain.Templates;
using MisaConnect.EInvoice.Infrastructure.Caching;
using MisaConnect.EInvoice.TestSupport.Fixtures;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.UseCases;

public class PreviewInvoiceTests
{
    private static readonly Template DefaultTemplate = new("ipt-1", "1C25MNQ", "Default", null, 1, true, true, false);

    [Fact]
    public async Task Ambiguous_template_rejects_without_MISA()
    {
        var sut = MakeSut(out var stub, new StubResolver(new TemplateResolution.AmbiguousTemplate(new[] { DefaultTemplate, DefaultTemplate })));
        var ex = await Assert.ThrowsAsync<MeInvoiceException>(() => sut.ExecuteAsync(SampleInvoices.TypicalVatInvoice(), true, default));
        Assert.Equal(MeInvoiceErrorCategory.Configuration, ex.Category);
        Assert.Equal("AmbiguousTemplate", ex.RawErrorCode);
        Assert.Equal(0, stub.PreviewCount);
    }

    [Fact]
    public async Task No_active_template_rejects_without_MISA()
    {
        var sut = MakeSut(out var stub, new StubResolver(new TemplateResolution.NoActiveTemplate()));
        var ex = await Assert.ThrowsAsync<MeInvoiceException>(() => sut.ExecuteAsync(SampleInvoices.TypicalVatInvoice(), true, default));
        Assert.Equal(MeInvoiceErrorCategory.TemplateState, ex.Category);
        Assert.Equal(0, stub.PreviewCount);
    }

    [Fact]
    public async Task Local_validation_failure_short_circuits_MISA()
    {
        var sut = MakeSut(out var stub);
        var invoice = SampleInvoices.TypicalVatInvoice();
        invoice = invoice with { Totals = invoice.Totals with { TotalDiscountAmountOC = 100m } };
        var ex = await Assert.ThrowsAsync<MeInvoiceException>(() => sut.ExecuteAsync(invoice, true, default));
        Assert.Equal(MeInvoiceErrorCategory.Validation, ex.Category);
        Assert.Equal(0, stub.PreviewCount);
    }

    [Fact]
    public async Task Service_generates_RefId_on_omission()
    {
        var sut = MakeSut(out var stub);
        var invoice = SampleInvoices.TypicalVatInvoice() with { RefId = default };
        await sut.ExecuteAsync(invoice, true, default);
        Assert.NotEmpty(stub.LastInvoice!.RefId.Value);
    }

    private static PreviewInvoice MakeSut(out StubClient client, ITemplateResolver? resolver = null)
    {
        client = new StubClient();
        resolver ??= new StubResolver(new TemplateResolution.Resolved(DefaultTemplate));
        var ensure = new EnsureAccessToken(client, new InMemoryTokenCache(), new SystemClock(TimeProvider.System), () => "key");
        return new PreviewInvoice(client, ensure, new InvoiceValidator(), resolver, new GuidRefIdGenerator());
    }

    private sealed class StubResolver : ITemplateResolver
    {
        private readonly TemplateResolution _result;
        public StubResolver(TemplateResolution result) => _result = result;
        public Task<TemplateResolution> ResolveAsync(TemplateRef? callerSupplied, bool invoiceWithCode, CancellationToken ct) =>
            Task.FromResult(_result);
    }

    private sealed class StubClient : IMeInvoiceClient
    {
        public int PreviewCount { get; private set; }
        public Invoice? LastInvoice { get; private set; }

        public Task<AccessToken> AcquireTokenAsync(CancellationToken ct) => Task.FromResult(new AccessToken("t", DateTimeOffset.UtcNow.AddDays(1)));
        public Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct) => Task.FromResult<IReadOnlyList<Template>>(new[] { DefaultTemplate });

        public Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct)
        {
            PreviewCount++;
            LastInvoice = invoice;
            return Task.FromResult(new PdfDocument(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 }));
        }

        public Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
    }
}

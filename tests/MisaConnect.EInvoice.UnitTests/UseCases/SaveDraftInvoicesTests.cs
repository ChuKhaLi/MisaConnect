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

public class SaveDraftInvoicesTests
{
    private static readonly Template DefaultTemplate = new("ipt-1", "1C25MNQ", "Default", null, 1, true, true, false);

    [Fact]
    public async Task Caller_omits_RefId_service_generates_GUID()
    {
        var sut = MakeSut(out var stub);
        var invoice = SampleInvoices.TypicalVatInvoice() with { RefId = default };
        var results = await sut.ExecuteAsync(new[] { invoice }, true, default);

        Assert.Single(results);
        Assert.Equal(SaveOutcome.Success, results[0].Outcome);
        Assert.NotEmpty(results[0].RefId.Value);
        Assert.Single(stub.LastBatch!.Invoices);
        Assert.NotEmpty(stub.LastBatch.Invoices[0].RefId.Value);
    }

    [Fact]
    public async Task Caller_supplied_RefId_forwarded_verbatim()
    {
        var sut = MakeSut(out var stub);
        var invoice = SampleInvoices.TypicalVatInvoice() with { RefId = RefId.From("my-custom-key") };
        var results = await sut.ExecuteAsync(new[] { invoice }, true, default);
        Assert.Equal("my-custom-key", results[0].RefId.Value);
        Assert.Equal("my-custom-key", stub.LastBatch!.Invoices[0].RefId.Value);
    }

    [Fact]
    public async Task Batch_size_31_rejected_before_MISA()
    {
        var sut = MakeSut(out var stub);
        var invoices = Enumerable.Repeat(SampleInvoices.TypicalVatInvoice(), 31).ToArray();
        var ex = await Assert.ThrowsAsync<MeInvoiceException>(() => sut.ExecuteAsync(invoices, true, default));
        Assert.Equal(MeInvoiceErrorCategory.Validation, ex.Category);
        Assert.Equal("InvoiceQuantityTooLarge", ex.RawErrorCode);
        Assert.Null(stub.LastBatch);
    }

    [Fact]
    public async Task Locally_rejected_invoices_never_reach_MISA()
    {
        var sut = MakeSut(out var stub);
        var good = SampleInvoices.TypicalVatInvoice();
        var bad = SampleInvoices.MultiCurrencyVatInvoice() with { ExchangeRate = 0m };
        var results = await sut.ExecuteAsync(new[] { good, bad }, true, default);

        Assert.Equal(SaveOutcome.Success, results[0].Outcome);
        Assert.Equal(SaveOutcome.Error, results[1].Outcome);
        Assert.Single(stub.LastBatch!.Invoices);
    }

    [Fact]
    public async Task All_locally_rejected_skips_MISA_call()
    {
        var sut = MakeSut(out var stub);
        var bad1 = SampleInvoices.MultiCurrencyVatInvoice() with { ExchangeRate = 0m };
        var typical = SampleInvoices.TypicalVatInvoice();
        var bad2 = typical with { PaymentMethod = "" };
        var results = await sut.ExecuteAsync(new[] { bad1, bad2 }, true, default);
        Assert.All(results, r => Assert.Equal(SaveOutcome.Error, r.Outcome));
        Assert.Null(stub.LastBatch);
    }

    [Fact]
    public async Task Ambiguous_template_rejects_without_MISA_call()
    {
        var resolver = new StubResolver(new TemplateResolution.AmbiguousTemplate(new[] { DefaultTemplate, DefaultTemplate }));
        var sut = MakeSut(out var stub, resolver);
        var invoice = SampleInvoices.TypicalVatInvoice() with { Template = null };
        var results = await sut.ExecuteAsync(new[] { invoice }, true, default);
        Assert.Equal(SaveOutcome.Error, results[0].Outcome);
        Assert.Equal(MeInvoiceErrorCategory.Configuration, results[0].Error!.Category);
        Assert.Null(stub.LastBatch);
    }

    private static SaveDraftInvoices MakeSut(out StubClient client, ITemplateResolver? resolver = null)
    {
        client = new StubClient();
        resolver ??= new StubResolver(new TemplateResolution.Resolved(DefaultTemplate));
        var ensure = new EnsureAccessToken(client, new InMemoryTokenCache(), new SystemClock(TimeProvider.System), () => "key");
        return new SaveDraftInvoices(client, ensure, new InvoiceValidator(), resolver, new GuidRefIdGenerator());
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
        public BatchSubmission? LastBatch { get; private set; }
        public Task<AccessToken> AcquireTokenAsync(CancellationToken ct) => Task.FromResult(new AccessToken("t", DateTimeOffset.UtcNow.AddDays(1)));
        public Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct) => Task.FromResult<IReadOnlyList<Template>>(new[] { DefaultTemplate });
        public Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();

        public Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct)
        {
            LastBatch = batch;
            var results = batch.Invoices.Select(i => new SaveResult(i.RefId, SaveOutcome.Success)).ToList();
            return Task.FromResult<IReadOnlyList<SaveResult>>(results);
        }

        public Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
    }
}

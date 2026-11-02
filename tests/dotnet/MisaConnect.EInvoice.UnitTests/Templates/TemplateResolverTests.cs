using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Application.Templates;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Pdf;
using MisaConnect.EInvoice.Domain.Templates;
using MisaConnect.EInvoice.Infrastructure.Caching;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Templates;

public class TemplateResolverTests
{
    [Fact]
    public async Task Single_active_with_omitted_ref_returns_Resolved()
    {
        var resolver = MakeResolver(new Template("id-1", "1C25MNQ", "Default", null, 1, true, true, false));
        var result = await resolver.ResolveAsync(callerSupplied: null, true, default);
        Assert.IsType<TemplateResolution.Resolved>(result);
    }

    [Fact]
    public async Task Two_actives_with_omitted_ref_returns_Ambiguous()
    {
        var resolver = MakeResolver(
            new Template("a", "1C25MNQ", "T1", null, 1, true, true, false),
            new Template("b", "2C25MNQ", "T2", null, 1, true, true, false));
        var result = await resolver.ResolveAsync(null, true, default);
        Assert.IsType<TemplateResolution.AmbiguousTemplate>(result);
    }

    [Fact]
    public async Task No_actives_with_omitted_ref_returns_NoActiveTemplate()
    {
        var resolver = MakeResolver();
        var result = await resolver.ResolveAsync(null, true, default);
        Assert.IsType<TemplateResolution.NoActiveTemplate>(result);
    }

    [Fact]
    public async Task Caller_ref_with_no_match_returns_UnknownTemplate()
    {
        var resolver = MakeResolver(new Template("a", "1C25MNQ", "T1", null, 1, true, true, false));
        var result = await resolver.ResolveAsync(new TemplateRef("zzz", "zzz"), true, default);
        Assert.IsType<TemplateResolution.UnknownTemplate>(result);
    }

    [Fact]
    public async Task Caller_exact_match_returns_Resolved()
    {
        var resolver = MakeResolver(new Template("a", "1C25MNQ", "T1", null, 1, true, true, false));
        var result = await resolver.ResolveAsync(new TemplateRef("a", "1C25MNQ"), true, default);
        Assert.IsType<TemplateResolution.Resolved>(result);
    }

    private static TemplateResolver MakeResolver(params Template[] templates)
    {
        var stub = new StubMeInvoiceClient(templates);
        var ensure = new EnsureAccessToken(stub, new InMemoryTokenCache(),
            new SystemClock(TimeProvider.System), () => "key");
        var list = new ListActiveTemplates(stub, ensure);
        return new TemplateResolver(list);
    }

    private sealed class StubMeInvoiceClient : IMeInvoiceClient
    {
        private readonly IReadOnlyList<Template> _templates;
        public StubMeInvoiceClient(IReadOnlyList<Template> t) => _templates = t;

        public Task<AccessToken> AcquireTokenAsync(CancellationToken ct) =>
            Task.FromResult(new AccessToken("t", DateTimeOffset.UtcNow.AddDays(1)));
        public Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct) => Task.FromResult(_templates);
        public Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
    }
}

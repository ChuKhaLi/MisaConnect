using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Pdf;
using MisaConnect.EInvoice.Domain.Templates;
using MisaConnect.EInvoice.Infrastructure.Caching;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.UseCases;

public class ListActiveTemplatesTests
{
    [Fact]
    public async Task Filters_inactive_templates()
    {
        var t1 = new Template("a", "1C25A", "Active", null, 1, true, true, false);
        var t2 = new Template("b", "2K25A", "Inactive", null, 1, false, false, false);
        var stub = new Stub(new[] { t1, t2 });
        var useCase = MakeUseCase(stub);

        var result = await useCase.ExecuteAsync(true, default);
        Assert.Single(result);
        Assert.Equal("a", result[0].IPTemplateID);
    }

    [Fact]
    public async Task Empty_response_returns_empty_list()
    {
        var useCase = MakeUseCase(new Stub(Array.Empty<Template>()));
        var result = await useCase.ExecuteAsync(true, default);
        Assert.Empty(result);
    }

    private static ListActiveTemplates MakeUseCase(IMeInvoiceClient client)
    {
        var ensure = new EnsureAccessToken(client, new InMemoryTokenCache(),
            new SystemClock(TimeProvider.System), () => "key");
        return new ListActiveTemplates(client, ensure);
    }

    private sealed class Stub : IMeInvoiceClient
    {
        private readonly IReadOnlyList<Template> _templates;
        public Stub(IReadOnlyList<Template> t) => _templates = t;

        public Task<AccessToken> AcquireTokenAsync(CancellationToken ct) =>
            Task.FromResult(new AccessToken("tok", DateTimeOffset.UtcNow.AddDays(1)));
        public Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct) => Task.FromResult(_templates);
        public Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
    }
}

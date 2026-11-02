using System.Net;
using System.Net.Http.Json;
using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Pdf;
using MisaConnect.EInvoice.Domain.Templates;
using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.Api;

public class TemplateEndpointsTests
{
    [Fact]
    public async Task GET_api_templates_returns_200_and_camelCase_json()
    {
        var stub = new StubClient(new[]
        {
            new Template("ipt-1", "1C25MNQ", "Default", "1", 1, true, true, false),
        });
        await using var factory = new WebFactory(stub);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "test-corr");

        using var response = await client.GetAsync("/api/templates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("test-corr", response.Headers.GetValues("X-Correlation-ID").Single());

        var payload = await response.Content.ReadFromJsonAsync<TemplatesResponse>();
        Assert.NotNull(payload);
        Assert.Single(payload!.templates);
        Assert.Equal("ipt-1", payload.templates[0].ipTemplateId);
    }

    private sealed record TemplatesResponse(List<TemplateEntry> templates);
    private sealed record TemplateEntry(string ipTemplateId, string invSeries, string templateName);

    private sealed class StubClient : IMeInvoiceClient
    {
        private readonly IReadOnlyList<Template> _templates;
        public StubClient(IReadOnlyList<Template> t) => _templates = t;

        public Task<AccessToken> AcquireTokenAsync(CancellationToken ct) =>
            Task.FromResult(new AccessToken("tok", DateTimeOffset.UtcNow.AddDays(1)));
        public Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct) => Task.FromResult(_templates);
        public Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
    }
}

using System.Net;
using System.Net.Http.Json;
using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Client.Dtos;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Pdf;
using MisaConnect.EInvoice.Domain.Templates;
using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.Api;

public class SaveDraftEndpointTests
{
    [Fact]
    public async Task POST_api_invoices_draft_returns_results_envelope()
    {
        var stub = new EchoClient();
        await using var factory = new WebFactory(stub);
        using var client = factory.CreateClient();

        var invoice = SampleInvoiceFactory.CreateDto(refId: "key-1");
        var invoice2 = SampleInvoiceFactory.CreateDto(refId: "key-2");

        using var response = await client.PostAsJsonAsync("/api/invoices/draft", new[] { invoice, invoice2 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.results.Count);
        Assert.All(payload.results, r => Assert.Equal("Success", r.outcome));
    }

    [Fact]
    public async Task POST_api_invoices_draft_batch_31_returns_400_with_FR_030()
    {
        var stub = new EchoClient();
        await using var factory = new WebFactory(stub);
        using var client = factory.CreateClient();

        var invoices = Enumerable.Range(0, 31).Select(i => SampleInvoiceFactory.CreateDto(refId: $"r-{i}")).ToArray();
        using var response = await client.PostAsJsonAsync("/api/invoices/draft", invoices);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("InvoiceQuantityTooLarge", body, StringComparison.Ordinal);
        Assert.Contains("FR-030", body, StringComparison.Ordinal);
    }

    private sealed record Envelope(List<ResultEntry> results);
    private sealed record ResultEntry(string refId, string outcome);

    private sealed class EchoClient : IMeInvoiceClient
    {
        private static readonly Template T = new("ipt-1", "1C25MNQ", "Default", null, 1, true, true, false);
        public Task<AccessToken> AcquireTokenAsync(CancellationToken ct) => Task.FromResult(new AccessToken("t", DateTimeOffset.UtcNow.AddDays(1)));
        public Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct) => Task.FromResult<IReadOnlyList<Template>>(new[] { T });
        public Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SaveResult>>(batch.Invoices.Select(i => new SaveResult(i.RefId, SaveOutcome.Success)).ToList());
        public Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
    }
}

internal static class SampleInvoiceFactory
{
    public static InvoiceDto CreateDto(string? refId = null)
    {
        return new InvoiceDto(
            RefId: refId,
            Template: null,
            InvDate: new DateOnly(2026, 5, 11),
            CreatedDate: DateTimeOffset.UtcNow,
            ModifiedDate: DateTimeOffset.UtcNow,
            Currency: "VND",
            ExchangeRate: 1m,
            PaymentMethod: "TM",
            BuyerType: 1,
            Buyer: new BuyerInfoDto("Buyer", "0123456789"),
            Lines: new[]
            {
                new InvoiceLineDto(
                    InventoryItemType: 0,
                    SortOrder: 1,
                    Description: "Goods",
                    UnitName: "Cái",
                    Quantity: 1m,
                    UnitPrice: 1_000_000m,
                    AmountOC: 1_000_000m,
                    Amount: 1_000_000m,
                    AmountWithoutVATOC: 1_000_000m,
                    AmountWithoutVAT: 1_000_000m,
                    VatRateName: "10%",
                    VATAmountOC: 100_000m,
                    VATAmount: 100_000m,
                    SortOrderView: 1)
            },
            Totals: new InvoiceTotalsDto(
                1_000_000m, 1_000_000m,
                0m, 0m,
                1_000_000m, 1_000_000m,
                100_000m, 100_000m,
                1_100_000m, 1_100_000m,
                "Một triệu một trăm nghìn"));
    }
}

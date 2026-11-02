using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MisaConnect.EInvoice.Client.Dtos;
using MisaConnect.EInvoice.IntegrationTests.Api;
using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.MisaFake;

/// <summary>
/// Slice 6 fake-server integration tests (T-RP-13, T-RP-14, T-AJ-14, T-AJ-15).
/// Runs in every CI build via in-process <see cref="FakeMisaServer"/>; no
/// sandbox reachability required.
/// </summary>
public class AmendmentFakeTests
{
    private static readonly OriginalInvoiceReferenceDto Ref = new(
        OrgRefID: "orig-001",
        OrgInvNo: "00000123",
        OrgInvTemplateNo: "1",
        OrgInvSeries: "C26TAA",
        OrgInvDate: new DateOnly(2026, 5, 10));

    [Fact]
    public async Task Replacement_round_trip_through_HTTP_surface_via_FakeMisaServer()
    {
        await using var fake = await FakeMisaServer.StartAsync();
        await using var factory = CreateFactory(fake.BaseUrl);
        using var client = factory.CreateClient();

        var invoice = SampleAmendmentDto.CreateInvoice(refId: "fresh-replacement-1");
        var request = new ReplacementRequestDto(
            Invoice: invoice,
            OriginalRef: Ref,
            ChangeReason: "Sua thong tin nguoi mua",
            InvoiceWithCode: true);

        using var response = await client.PostAsJsonAsync("/api/invoices/replacement?withCode=true", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var insertSnapshot = fake.CapturedRequests.First(r => r.Path == "/webapp/insert");
        var json = JsonDocument.Parse(insertSnapshot.Body).RootElement;
        Assert.Equal(JsonValueKind.Array, json.ValueKind);
        var first = json[0];
        Assert.Equal("3", first.GetProperty("EInvoiceStatus").GetString());
        Assert.Equal("orig-001", first.GetProperty("OrgRefID").GetString());
        Assert.Equal("00000123", first.GetProperty("OrgInvNo").GetString());
        Assert.Equal("1", first.GetProperty("OrgInvTemplateNo").GetString());
        Assert.Equal("C26TAA", first.GetProperty("OrgInvSeries").GetString());
        Assert.Equal("2026-05-10", first.GetProperty("OrgInvDate").GetString());
        Assert.Equal("Sua thong tin nguoi mua", first.GetProperty("ChangeReason").GetString());

        var body = await response.Content.ReadFromJsonAsync<AmendmentResultDto>();
        Assert.NotNull(body);
        Assert.Equal(SaveOutcomeDto.Success, body!.Outcome);
        Assert.Equal("fresh-replacement-1", body.RefId);
        Assert.Equal("orig-001", body.OrgRefId);
    }

    [Fact]
    public async Task Replacement_with_HasAdjustmentInvoice_seed_returns_409_Conflict()
    {
        await using var fake = await FakeMisaServer.StartAsync();
        fake.SeedInsertResponse("fresh-replacement-2", InsertSeedResult.PerEntryError(
            "fresh-replacement-2", "HasAdjustmentInvoice", "Original already has an adjustment."));
        await using var factory = CreateFactory(fake.BaseUrl);
        using var client = factory.CreateClient();

        var invoice = SampleAmendmentDto.CreateInvoice(refId: "fresh-replacement-2");
        var request = new ReplacementRequestDto(invoice, Ref, "Sua", true);

        using var response = await client.PostAsJsonAsync("/api/invoices/replacement?withCode=true", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AmendmentResultDto>();
        Assert.NotNull(body);
        Assert.Equal("Replacement", body!.ErrorCategory);
        Assert.Equal("HasAdjustmentInvoice", body.RawErrorCode);
    }

    [Fact]
    public async Task Adjustment_round_trip_through_HTTP_surface_via_FakeMisaServer()
    {
        await using var fake = await FakeMisaServer.StartAsync();
        await using var factory = CreateFactory(fake.BaseUrl);
        using var client = factory.CreateClient();

        var invoice = SampleAmendmentDto.CreateDeltaInvoice(refId: "fresh-adjustment-1", amount: 100_000m);
        var request = new AdjustmentRequestDto(
            Invoice: invoice,
            OriginalRef: Ref,
            ChangeReason: "Bo sung phu thu",
            InvoiceWithCode: true);

        using var response = await client.PostAsJsonAsync("/api/invoices/adjustment?withCode=true", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var insertSnapshot = fake.CapturedRequests.First(r => r.Path == "/webapp/insert");
        var json = JsonDocument.Parse(insertSnapshot.Body).RootElement;
        var first = json[0];
        Assert.Equal("4", first.GetProperty("EInvoiceStatus").GetString());
        Assert.Equal("orig-001", first.GetProperty("OrgRefID").GetString());
        Assert.Equal("Bo sung phu thu", first.GetProperty("ChangeReason").GetString());
    }

    [Fact]
    public async Task Adjustment_with_negative_delta_serialises_negatives_verbatim()
    {
        await using var fake = await FakeMisaServer.StartAsync();
        await using var factory = CreateFactory(fake.BaseUrl);
        using var client = factory.CreateClient();

        var invoice = SampleAmendmentDto.CreateDeltaInvoice(refId: "fresh-adjustment-2", amount: -55_000m);
        var request = new AdjustmentRequestDto(invoice, Ref, "Giam tru phu thu", true);

        using var response = await client.PostAsJsonAsync("/api/invoices/adjustment?withCode=true", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var insertSnapshot = fake.CapturedRequests.First(r => r.Path == "/webapp/insert");
        var json = JsonDocument.Parse(insertSnapshot.Body).RootElement;
        var first = json[0];
        Assert.Equal(-55_000m, first.GetProperty("TotalAmount").GetDecimal());
        Assert.True(first.GetProperty("TotalAmount").GetDecimal() < 0);
    }

    private static WebFactory CreateFactory(string fakeBaseUrl) =>
        new WebFactory().WithConfiguration(config =>
        {
            config["Misa:EInvoice:Environment"] = "Sandbox";
            config["Misa:EInvoice:BaseUrl"] = "https://testapi.meinvoice.vn/api/integration";
            config["Misa:EInvoice:TaxCode"] = "0000000000";
            config["Misa:EInvoice:AppId"] = "267";
            config["Misa:EInvoice:UserName"] = "u";
            config["Misa:EInvoice:Password"] = "p";
            config["Misa:EInvoice:Delete:IncludeRawErrorMessage"] = "false";
        }).WithBaseAddressOverride(fakeBaseUrl);
}

internal static class SampleAmendmentDto
{
    public static InvoiceDto CreateInvoice(string refId)
    {
        return new InvoiceDto(
            RefId: refId,
            Template: null,
            InvDate: new DateOnly(2026, 5, 13),
            CreatedDate: new DateTimeOffset(2026, 5, 13, 8, 30, 0, TimeSpan.Zero),
            ModifiedDate: new DateTimeOffset(2026, 5, 13, 8, 30, 0, TimeSpan.Zero),
            Currency: "VND",
            ExchangeRate: 1m,
            PaymentMethod: "TM",
            BuyerType: 1,
            Buyer: new BuyerInfoDto("Cong ty TNHH Mua", "0123456789"),
            Lines: new[]
            {
                new InvoiceLineDto(
                    InventoryItemType: 0,
                    SortOrder: 1,
                    Description: "Goods",
                    UnitName: "Cai",
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
                "Mot trieu mot tram nghin"));
    }

    public static InvoiceDto CreateDeltaInvoice(string refId, decimal amount)
    {
        return new InvoiceDto(
            RefId: refId,
            Template: null,
            InvDate: new DateOnly(2026, 5, 13),
            CreatedDate: new DateTimeOffset(2026, 5, 13, 8, 30, 0, TimeSpan.Zero),
            ModifiedDate: new DateTimeOffset(2026, 5, 13, 8, 30, 0, TimeSpan.Zero),
            Currency: "VND",
            ExchangeRate: 1m,
            PaymentMethod: "TM",
            BuyerType: 1,
            Buyer: new BuyerInfoDto("Cong ty TNHH Mua", "0123456789"),
            Lines: new[]
            {
                new InvoiceLineDto(
                    InventoryItemType: 0,
                    SortOrder: 1,
                    Description: "Dieu chinh",
                    UnitName: "Lan",
                    Quantity: 1m,
                    UnitPrice: amount,
                    AmountOC: amount,
                    Amount: amount,
                    AmountWithoutVATOC: amount,
                    AmountWithoutVAT: amount,
                    VatRateName: null,
                    VATAmountOC: 0m,
                    VATAmount: 0m,
                    SortOrderView: 1)
            },
            Totals: new InvoiceTotalsDto(
                amount, amount,
                0m, 0m,
                amount, amount,
                0m, 0m,
                amount, amount,
                "Delta"));
    }
}

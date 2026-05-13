using System.Net;
using System.Net.Http.Json;
using MisaConnect.EInvoice.Client.Dtos;
using MisaConnect.EInvoice.IntegrationTests.Api;
using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.MisaFake;

/// <summary>
/// Slice 5 fake-server tests T26 + T27 from
/// <c>contracts/lookup-paginated.md</c> — exercises the POS-calculating
/// paginated lookup against the in-process <see cref="FakeMisaServer"/>.
/// Runs in every CI build (no sandbox reachability required) per FR-054 /
/// SC-006. Verifies the calculating endpoint is routed to
/// <c>POST /webapp/paging/calculating</c> AND the calculating-vs-standard
/// disjointness contract observed in production MISA (FR-046).
/// </summary>
public class LookupPagedCalculatingFakeTests
{
    [Fact]
    public async Task FakeServer_calculating_paging_returns_calculating_snapshots()
    {
        await using var fake = await FakeMisaServer.StartAsync();
        fake
            .SeedCalculatingInvoice(new SeededSnapshot(
                RefID: "c1", InvDate: "2026-05-10", PublishStatus: "0", InvSeries: "1C26MAA"))
            .SeedCalculatingInvoice(new SeededSnapshot(
                RefID: "c2", InvDate: "2026-05-11", PublishStatus: "0", InvSeries: "1C26MAA"))
            .SeedCalculatingInvoice(new SeededSnapshot(
                RefID: "c3", InvDate: "2026-05-12", PublishStatus: "0", InvSeries: "1C26MAA"));

        await using var factory = CreateFactory(fake.BaseUrl);
        using var client = factory.CreateClient();

        var request = new PagedLookupRequestDto(
            Start: 0,
            Length: 100,
            Sort: "InvDate",
            FromDate: new DateOnly(2026, 5, 1),
            ToDate: new DateOnly(2026, 5, 31),
            PublishStatus: null);

        using var response = await client.PostAsJsonAsync(
            "/api/invoices/lookup/calculating?withCode=true", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PagedLookupResultDto>();
        Assert.NotNull(body);
        Assert.Equal(3, body!.ReturnedCount);
        Assert.Equal(3, body.Items.Count);
        Assert.Contains(body.Items, i => i.RefId == "c1");
        Assert.Contains(body.Items, i => i.RefId == "c2");
        Assert.Contains(body.Items, i => i.RefId == "c3");
    }

    [Fact]
    public async Task FakeServer_standard_and_calculating_disjoint()
    {
        await using var fake = await FakeMisaServer.StartAsync();
        fake
            .SeedStandardInvoice(new SeededSnapshot(
                RefID: "s1", InvSeries: "1C26TAA", InvDate: "2026-05-10", PublishStatus: "0"))
            .SeedCalculatingInvoice(new SeededSnapshot(
                RefID: "c1", InvSeries: "1C26MAA", InvDate: "2026-05-10", PublishStatus: "0"));

        await using var factory = CreateFactory(fake.BaseUrl);
        using var client = factory.CreateClient();

        var request = new PagedLookupRequestDto(
            Start: 0,
            Length: 100,
            Sort: "InvDate",
            FromDate: new DateOnly(2026, 5, 1),
            ToDate: new DateOnly(2026, 5, 31),
            PublishStatus: null);

        // /lookup/standard MUST return only the standard seed.
        using var standardResponse = await client.PostAsJsonAsync(
            "/api/invoices/lookup/standard?withCode=true", request);
        Assert.Equal(HttpStatusCode.OK, standardResponse.StatusCode);
        var standardBody = await standardResponse.Content.ReadFromJsonAsync<PagedLookupResultDto>();
        Assert.NotNull(standardBody);
        Assert.Single(standardBody!.Items);
        Assert.Equal("s1", standardBody.Items[0].RefId);

        // /lookup/calculating MUST return only the calculating seed.
        using var calculatingResponse = await client.PostAsJsonAsync(
            "/api/invoices/lookup/calculating?withCode=true", request);
        Assert.Equal(HttpStatusCode.OK, calculatingResponse.StatusCode);
        var calculatingBody = await calculatingResponse.Content.ReadFromJsonAsync<PagedLookupResultDto>();
        Assert.NotNull(calculatingBody);
        Assert.Single(calculatingBody!.Items);
        Assert.Equal("c1", calculatingBody.Items[0].RefId);
    }

    private static WebFactory CreateFactory(string fakeBaseUrl)
    {
        return new WebFactory().WithConfiguration(config =>
        {
            config["Misa:EInvoice:Environment"] = "Sandbox";
            // Slice 1 host-validator requires the configured BaseUrl to match
            // the canonical Sandbox host; the fake server lives on 127.0.0.1
            // so we keep the canonical URL for validation and override the
            // typed HttpClient's BaseAddress to point at the fake.
            config["Misa:EInvoice:BaseUrl"] = "https://testapi.meinvoice.vn/api/integration";
            config["Misa:EInvoice:TaxCode"] = "0000000000";
            config["Misa:EInvoice:AppId"] = "267";
            config["Misa:EInvoice:UserName"] = "u";
            config["Misa:EInvoice:Password"] = "p";
        }).WithBaseAddressOverride(fakeBaseUrl);
    }
}

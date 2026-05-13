using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MisaConnect.EInvoice.Client.Dtos;
using MisaConnect.EInvoice.IntegrationTests.Api;
using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.MisaFake;

/// <summary>
/// Slice 5 US2 fake-server tests T23–T25 + T28–T29 from
/// <c>specs/005-misa-invoice-lookup/contracts/lookup-paginated.md</c>. The
/// in-process <see cref="FakeMisaServer"/> stands in for MISA's
/// <c>POST /webapp/paging</c> endpoint; the API talks to it through the real
/// <c>MeInvoiceClient</c> + <c>BearerTokenHandler</c> + <c>ThrottleRetryHandler</c>
/// chain.
///
/// Runs in EVERY CI build — no sandbox reachability required.
/// FR-054 / SC-006.
/// </summary>
public class LookupPagedFakeTests
{
    [Fact]
    public async Task FakeServer_standard_paging_returns_seeded_snapshots()
    {
        await using var fake = await FakeMisaServer.StartAsync();
        fake
            .SeedStandardInvoice(new SeededSnapshot(
                RefID: "s1", InvDate: "2026-05-10", PublishStatus: "0", EInvoiceStatus: "1"))
            .SeedStandardInvoice(new SeededSnapshot(
                RefID: "s2", InvDate: "2026-05-15", PublishStatus: "0", EInvoiceStatus: "1"))
            .SeedStandardInvoice(new SeededSnapshot(
                RefID: "s3", InvDate: "2026-05-20", PublishStatus: "0", EInvoiceStatus: "1"));

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
            "/api/invoices/lookup/standard?withCode=true", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PagedLookupResultDto>();
        Assert.NotNull(body);
        Assert.Equal(3, body!.ReturnedCount);
        Assert.Equal(3, body.Items.Count);
        Assert.Contains(body.Items, i => i.RefId == "s1");
        Assert.Contains(body.Items, i => i.RefId == "s2");
        Assert.Contains(body.Items, i => i.RefId == "s3");
    }

    [Fact]
    public async Task FakeServer_standard_paging_with_PublishStatus_filter()
    {
        await using var fake = await FakeMisaServer.StartAsync();
        fake
            .SeedStandardInvoice(new SeededSnapshot(
                RefID: "draft-1", InvDate: "2026-05-10", PublishStatus: "0", EInvoiceStatus: "1"))
            .SeedStandardInvoice(new SeededSnapshot(
                RefID: "draft-2", InvDate: "2026-05-11", PublishStatus: "0", EInvoiceStatus: "1"))
            .SeedStandardInvoice(new SeededSnapshot(
                RefID: "issued-1", InvDate: "2026-05-12", PublishStatus: "4", EInvoiceStatus: "3"));

        await using var factory = CreateFactory(fake.BaseUrl);
        using var client = factory.CreateClient();

        var request = new PagedLookupRequestDto(
            Start: 0,
            Length: 100,
            Sort: "InvDate",
            FromDate: new DateOnly(2026, 5, 1),
            ToDate: new DateOnly(2026, 5, 31),
            PublishStatus: 0);

        using var response = await client.PostAsJsonAsync(
            "/api/invoices/lookup/standard?withCode=true", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PagedLookupResultDto>();
        Assert.NotNull(body);
        Assert.Equal(2, body!.ReturnedCount);
        Assert.All(body.Items, item => Assert.Equal(0, item.RawPublishStatus));
    }

    [Fact]
    public async Task FakeServer_paging_traversal_to_end()
    {
        await using var fake = await FakeMisaServer.StartAsync();
        for (var i = 0; i < 250; i++)
        {
            fake.SeedStandardInvoice(new SeededSnapshot(
                RefID: $"r{i:D3}",
                InvDate: "2026-05-15",
                PublishStatus: "0",
                EInvoiceStatus: "1"));
        }

        await using var factory = CreateFactory(fake.BaseUrl);
        using var client = factory.CreateClient();

        var dateRange = (FromDate: new DateOnly(2026, 5, 1), ToDate: new DateOnly(2026, 5, 31));

        var page1 = await PostPage(client, start: 0, length: 100, dateRange);
        Assert.Equal(100, page1!.ReturnedCount);
        Assert.Equal(100, page1.Items.Count);

        var page2 = await PostPage(client, start: 100, length: 100, dateRange);
        Assert.Equal(100, page2!.ReturnedCount);
        Assert.Equal(100, page2.Items.Count);

        var page3 = await PostPage(client, start: 200, length: 100, dateRange);
        Assert.Equal(50, page3!.ReturnedCount);
        Assert.True(page3.ReturnedCount < page3.Length, "Last page must signal end-of-results via ReturnedCount < Length.");

        var observed = page1.Items.Concat(page2.Items).Concat(page3.Items).Select(i => i.RefId).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(250, observed.Count);
    }

    [Fact]
    public async Task FakeServer_Validation_failure_returns_400_with_fieldPath()
    {
        await using var fake = await FakeMisaServer.StartAsync();
        await using var factory = CreateFactory(fake.BaseUrl);
        using var client = factory.CreateClient();

        var request = new PagedLookupRequestDto(
            Start: 0,
            Length: 200, // out of range — FR-047 rejects
            Sort: "InvDate",
            FromDate: new DateOnly(2026, 5, 1),
            ToDate: new DateOnly(2026, 5, 31),
            PublishStatus: null);

        using var response = await client.PostAsJsonAsync(
            "/api/invoices/lookup/standard?withCode=true", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("Validation", doc.RootElement.GetProperty("errorCategory").GetString());
        var failures = doc.RootElement.GetProperty("failures");
        Assert.Equal(JsonValueKind.Array, failures.ValueKind);
        Assert.True(failures.GetArrayLength() >= 1);
        Assert.Equal("Length", failures[0].GetProperty("fieldPath").GetString());
    }

    [Fact]
    public async Task FakeServer_AuthFailed_returns_401()
    {
        await using var fake = await FakeMisaServer.StartAsync();
        // MISA's wire envelope for an auth failure: success=false +
        // errorCode="UnAuthorize". The MeInvoiceErrorMapper maps that raw code
        // to MeInvoiceErrorCategory.Authentication; the API ErrorMapping then
        // returns HTTP 401 with errorCategory="Authentication" (the .ToString()
        // of the domain enum). Contract wording "AuthFailed" describes the
        // semantic bucket; the actual emitted string matches the enum name.
        fake.SetPagingOverride((_, _) =>
            (200, "{\"success\":false,\"errorCode\":\"UnAuthorize\",\"ErrorMessage\":null}"));

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
            "/api/invoices/lookup/standard?withCode=true", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("Authentication", doc.RootElement.GetProperty("errorCategory").GetString());
    }

    private static async Task<PagedLookupResultDto?> PostPage(
        HttpClient client,
        int start,
        int length,
        (DateOnly FromDate, DateOnly ToDate) range)
    {
        var request = new PagedLookupRequestDto(
            Start: start,
            Length: length,
            Sort: "InvDate",
            FromDate: range.FromDate,
            ToDate: range.ToDate,
            PublishStatus: null);

        using var response = await client.PostAsJsonAsync(
            "/api/invoices/lookup/standard?withCode=true", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<PagedLookupResultDto>();
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

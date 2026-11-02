using System.Net;
using System.Net.Http.Json;
using MisaConnect.EInvoice.Client.Dtos;
using MisaConnect.EInvoice.IntegrationTests.Api;
using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.MisaFake;

/// <summary>
/// Spec contract tests T23–T26 from <c>contracts/lookup-by-refid.md</c>.
/// The API talks through the real <c>MeInvoiceClient</c> +
/// <c>BearerTokenHandler</c> + <c>ThrottleRetryHandler</c> chain into the
/// in-process <see cref="FakeMisaServer"/>. Runs in EVERY CI build — no
/// sandbox reachability required.
/// </summary>
public class LookupByRefIdFakeTests
{
    [Fact]
    public async Task FakeServer_seeds_three_RefIDs_returns_three_Found()
    {
        await using var fake = await FakeMisaServer.StartAsync();
        fake.SeedStandardInvoice(new SeededSnapshot(
            RefID: "ref-draft",
            EInvoiceStatus: "1",
            PublishStatus: "0"));
        fake.SeedStandardInvoice(new SeededSnapshot(
            RefID: "ref-signed",
            EInvoiceStatus: "1",
            PublishStatus: "4"));
        fake.SeedStandardInvoice(new SeededSnapshot(
            RefID: "ref-issued",
            EInvoiceStatus: "1",
            PublishStatus: "6"));

        await using var factory = CreateFactory(fake.BaseUrl);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/invoices/lookup/by-refid?withCode=true",
            new[] { "ref-draft", "ref-signed", "ref-issued" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LookupBatchOutcomeDto>();
        Assert.NotNull(body);
        Assert.Equal("Completed", body!.Status);
        Assert.NotNull(body.Outcomes);
        Assert.Equal(3, body.Outcomes!.Count);

        var byRef = body.Outcomes.ToDictionary(o => o.RefId, StringComparer.Ordinal);
        Assert.Equal("Found", byRef["ref-draft"].Status);
        Assert.Equal("Found", byRef["ref-signed"].Status);
        Assert.Equal("Found", byRef["ref-issued"].Status);
        Assert.NotNull(byRef["ref-draft"].Snapshot);
        Assert.Equal("Draft", byRef["ref-draft"].Snapshot!.Status);
        Assert.Equal("Signed", byRef["ref-signed"].Snapshot!.Status);
        Assert.Equal("Issued", byRef["ref-issued"].Snapshot!.Status);
    }

    [Fact]
    public async Task FakeServer_137_RefIDs_uses_three_chunks_observable_via_request_log()
    {
        await using var fake = await FakeMisaServer.StartAsync();
        var refIds = new string[137];
        for (var i = 0; i < 137; i++)
        {
            refIds[i] = $"r-{i:D4}";
            fake.SeedStandardInvoice(new SeededSnapshot(
                RefID: refIds[i],
                EInvoiceStatus: "1",
                PublishStatus: "0"));
        }

        await using var factory = CreateFactory(fake.BaseUrl);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/invoices/lookup/by-refid?withCode=true",
            refIds);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, fake.GetListCallCount);
        var body = await response.Content.ReadFromJsonAsync<LookupBatchOutcomeDto>();
        Assert.NotNull(body);
        Assert.Equal("Completed", body!.Status);
        Assert.NotNull(body.Outcomes);
        Assert.Equal(137, body.Outcomes!.Count);
        Assert.All(body.Outcomes, o => Assert.Equal("Found", o.Status));
    }

    [Fact]
    public async Task FakeServer_AuthFailed_on_chunk_2_short_circuits_third_chunk()
    {
        await using var fake = await FakeMisaServer.StartAsync();
        var refIds = new string[137];
        for (var i = 0; i < 137; i++)
        {
            refIds[i] = $"r-{i:D4}";
            fake.SeedStandardInvoice(new SeededSnapshot(
                RefID: refIds[i],
                EInvoiceStatus: "1",
                PublishStatus: "0"));
        }

        fake.SetGetListOverride((idx, body) =>
            idx == 2
                ? (200, """{"success":false,"errorCode":"UnAuthorize","descriptionErrorCode":null,"errors":[],"data":[]}""")
                : null);

        await using var factory = CreateFactory(fake.BaseUrl);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/invoices/lookup/by-refid?withCode=true",
            refIds);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Cascade rule: AuthFailed is terminal → chunk 3 is NOT called.
        Assert.Equal(2, fake.GetListCallCount);

        var body = await response.Content.ReadFromJsonAsync<LookupBatchOutcomeDto>();
        Assert.NotNull(body);
        Assert.Equal("Completed", body!.Status);
        Assert.NotNull(body.Outcomes);
        Assert.Equal(137, body.Outcomes!.Count);
        // First 50 are Found; the remaining 87 (chunk 2 + short-circuited chunk 3)
        // surface as AuthFailed.
        for (var i = 0; i < 50; i++)
        {
            Assert.Equal("Found", body.Outcomes![i].Status);
        }
        for (var i = 50; i < 137; i++)
        {
            Assert.Equal("AuthFailed", body.Outcomes![i].Status);
        }
    }

    [Fact]
    public async Task FakeServer_MisaThrottled_on_chunk_2_continues_to_chunk_3()
    {
        // Cascade transient category — Slice 1's MeInvoiceErrorMapper maps the
        // raw "Exception" wire-level error code to MisaUnavailable. Both
        // MisaThrottled and MisaUnavailable share the transient-cascade class
        // per R-LU-03: continue with remaining chunks. The test name preserves
        // the contract's intent (transient continuation) while using the
        // wire-mappable error code.
        await using var fake = await FakeMisaServer.StartAsync();
        var refIds = new string[137];
        for (var i = 0; i < 137; i++)
        {
            refIds[i] = $"r-{i:D4}";
            fake.SeedStandardInvoice(new SeededSnapshot(
                RefID: refIds[i],
                EInvoiceStatus: "1",
                PublishStatus: "0"));
        }

        fake.SetGetListOverride((idx, body) =>
            idx == 2
                ? (200, """{"success":false,"errorCode":"Exception","descriptionErrorCode":null,"errors":[],"data":[]}""")
                : null);

        await using var factory = CreateFactory(fake.BaseUrl);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/invoices/lookup/by-refid?withCode=true",
            refIds);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Cascade rule: transient category → chunk 3 IS called.
        Assert.Equal(3, fake.GetListCallCount);

        var body = await response.Content.ReadFromJsonAsync<LookupBatchOutcomeDto>();
        Assert.NotNull(body);
        Assert.Equal("Completed", body!.Status);
        Assert.NotNull(body.Outcomes);
        Assert.Equal(137, body.Outcomes!.Count);
        // Chunk 1 (0..49) and chunk 3 (100..136) Found; chunk 2 (50..99) errored.
        for (var i = 0; i < 50; i++)
            Assert.Equal("Found", body.Outcomes![i].Status);
        for (var i = 50; i < 100; i++)
            Assert.Equal("MisaUnavailable", body.Outcomes![i].Status);
        for (var i = 100; i < 137; i++)
            Assert.Equal("Found", body.Outcomes![i].Status);
    }

    private static Api.WebFactory CreateFactory(string fakeBaseUrl)
    {
        return new Api.WebFactory().WithConfiguration(config =>
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
}

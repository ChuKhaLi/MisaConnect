using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.MisaFake;

/// <summary>
/// Slice 2 T035 + T026 + T027 (tests T25/T26/T27 from
/// <c>contracts/delete-draft.md</c>). The fake MISA HTTP server runs
/// in-process; the API talks to it through the real <c>MeInvoiceClient</c>
/// + <c>BearerTokenHandler</c> + <c>ThrottleRetryHandler</c> chain.
///
/// Runs in EVERY CI build — no sandbox reachability required.
/// FR-040(b) + SC-007.
/// </summary>
public class DeleteDraftRejectionTests
{
    [Fact]
    public async Task FakeMisa_returns_PhatHanh_then_outcome_is_NotDeletable()
    {
        await using var fake = await FakeMisaServer.StartAsync();
        fake.SetDeleteResponse(200,
            """{"success":false,"errorCode":"InvalidTransactionID","ErrorMessage":"Hóa đơn đã phát hành nên không thể xóa."}""");

        await using var factory = CreateFactory(fake.BaseUrl, includeRawErrorMessage: false);
        using var client = factory.CreateClient();

        using var response = await client.DeleteAsync("/api/invoices/abc-123?withCode=true");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DeleteResponseEnvelope>();
        Assert.NotNull(body);
        Assert.Equal("NotDeletable", body!.status);
        Assert.Equal("InvalidTransactionID", body.errorCode);
        Assert.Null(body.message);
    }

    [Fact]
    public async Task FakeMisa_empty_ErrorMessage_defaults_to_NotFound()
    {
        await using var fake = await FakeMisaServer.StartAsync();
        fake.SetDeleteResponse(200,
            """{"success":false,"errorCode":"InvalidTransactionID","ErrorMessage":""}""");

        await using var factory = CreateFactory(fake.BaseUrl, includeRawErrorMessage: false);
        using var client = factory.CreateClient();

        using var response = await client.DeleteAsync("/api/invoices/abc-123?withCode=true");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DeleteResponseEnvelope>();
        Assert.Equal("NotFound", body!.status);
    }

    [Fact]
    public async Task Flag_on_surfaces_raw_message()
    {
        await using var fake = await FakeMisaServer.StartAsync();
        fake.SetDeleteResponse(200,
            """{"success":false,"errorCode":"InvalidTransactionID","ErrorMessage":"Hóa đơn đã phát hành nên không thể xóa."}""");

        await using var factory = CreateFactory(fake.BaseUrl, includeRawErrorMessage: true);
        using var client = factory.CreateClient();

        using var response = await client.DeleteAsync("/api/invoices/abc-123?withCode=true");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DeleteResponseEnvelope>();
        Assert.Equal("Hóa đơn đã phát hành nên không thể xóa.", body!.message);
    }

    [Fact]
    public async Task Wire_request_carries_Authorization_and_taxcode_headers()
    {
        await using var fake = await FakeMisaServer.StartAsync();
        fake.SetDeleteResponse(200, """{"success":true}""");

        await using var factory = CreateFactory(fake.BaseUrl, includeRawErrorMessage: false);
        using var client = factory.CreateClient();

        using var response = await client.DeleteAsync("/api/invoices/abc-123?withCode=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var deleteRequest = fake.CapturedRequests.Single(r => r.Path == "/webapp/delete");
        Assert.True(deleteRequest.Headers.ContainsKey("Authorization"), "Authorization header missing");
        Assert.StartsWith("Bearer ", deleteRequest.Headers["Authorization"]);
        Assert.True(deleteRequest.Headers.ContainsKey("taxcode"), "taxcode header missing");
        Assert.Equal("0000000000-267", deleteRequest.Headers["taxcode"]);
    }

    private static Api.WebFactory CreateFactory(string fakeBaseUrl, bool includeRawErrorMessage)
    {
        return new Api.WebFactory().WithConfiguration(config =>
        {
            config["Misa:EInvoice:Environment"] = "Sandbox";
            // Slice 1 validator requires the BaseUrl host to match the environment's
            // canonical host (testapi.meinvoice.vn for Sandbox). The fake server lives
            // on 127.0.0.1, so the API host validator would otherwise reject it.
            // We retain the canonical BaseUrl for validation and override the typed
            // HttpClient's BaseAddress after the host is built.
            config["Misa:EInvoice:BaseUrl"] = "https://testapi.meinvoice.vn/api/integration";
            config["Misa:EInvoice:TaxCode"] = "0000000000";
            config["Misa:EInvoice:AppId"] = "267";
            config["Misa:EInvoice:UserName"] = "u";
            config["Misa:EInvoice:Password"] = "p";
            config["Misa:EInvoice:Delete:IncludeRawErrorMessage"] = includeRawErrorMessage ? "true" : "false";
        }).WithBaseAddressOverride(fakeBaseUrl);
    }

    private sealed record DeleteResponseEnvelope(string? refId, string status, string? errorCode, string? field, string? message);
}

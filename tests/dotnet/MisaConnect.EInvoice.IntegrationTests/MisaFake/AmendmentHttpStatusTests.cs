using System.Net;
using System.Net.Http.Json;
using MisaConnect.EInvoice.Client.Dtos;
using MisaConnect.EInvoice.IntegrationTests.Api;
using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.MisaFake;

/// <summary>
/// Slice 6 (T084) — verifies the HTTP status code matrix from
/// <c>contracts/replacement.md</c> §HTTP status code matrix (shared
/// verbatim by <c>adjustment.md</c>). One assertion per
/// <c>ErrorCategory</c> → HTTP-status row, including the two new
/// <c>Replacement → 409</c> and <c>DuplicateOrUniqueness → 409</c>
/// cases introduced by slice 6.
/// </summary>
public class AmendmentHttpStatusTests
{
    private static readonly OriginalInvoiceReferenceDto Ref = new(
        OrgRefID: "orig-001",
        OrgInvNo: "00000123",
        OrgInvTemplateNo: "1",
        OrgInvSeries: "C26TAA",
        OrgInvDate: new DateOnly(2026, 5, 10));

    [Theory]
    [InlineData("HasAdjustmentInvoice", 409, "Replacement")]
    [InlineData("InvoiceCannotReplace", 409, "Replacement")]
    [InlineData("InvoiceCannotAdjust", 409, "Replacement")]
    [InlineData("InvoiceCannotReplaceByStatusNew", 409, "Replacement")]
    [InlineData("DuplicateInvoiceRefID", 409, "DuplicateOrUniqueness")]
    [InlineData("UnAuthorize", 401, "Authentication")]
    [InlineData("InvalidAppID", 400, "Configuration")]
    [InlineData("RefIdNotFound", 404, "ResourceNotFound")]
    [InlineData("Exception", 502, "MisaUnavailable")]
    [InlineData("SignatureEmpty", 502, "Signing")]
    public async Task Per_entry_error_maps_to_documented_HTTP_status(string rawErrorCode, int expectedStatus, string expectedCategory)
    {
        await using var fake = await FakeMisaServer.StartAsync();
        const string freshRefId = "fresh-status-test";
        fake.SeedInsertResponse(freshRefId,
            InsertSeedResult.PerEntryError(freshRefId, rawErrorCode, "fake error"));

        await using var factory = CreateFactory(fake.BaseUrl);
        using var client = factory.CreateClient();

        var invoice = SampleAmendmentDto.CreateInvoice(freshRefId);
        var request = new ReplacementRequestDto(invoice, Ref, "Sua", true);

        using var response = await client.PostAsJsonAsync("/api/invoices/replacement?withCode=true", request);

        Assert.Equal(expectedStatus, (int)response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AmendmentResultDto>();
        Assert.NotNull(body);
        Assert.Equal(expectedCategory, body!.ErrorCategory);
        Assert.Equal(rawErrorCode, body.RawErrorCode);
    }

    [Fact]
    public async Task Success_path_returns_200_with_orgRefId_echo()
    {
        await using var fake = await FakeMisaServer.StartAsync();
        await using var factory = CreateFactory(fake.BaseUrl);
        using var client = factory.CreateClient();

        var invoice = SampleAmendmentDto.CreateInvoice("fresh-ok");
        var request = new ReplacementRequestDto(invoice, Ref, "Sua", true);

        using var response = await client.PostAsJsonAsync("/api/invoices/replacement?withCode=true", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AmendmentResultDto>();
        Assert.NotNull(body);
        Assert.Equal(SaveOutcomeDto.Success, body!.Outcome);
        Assert.Equal("orig-001", body.OrgRefId);
    }

    [Fact]
    public async Task Validation_failure_returns_400_with_failures_array()
    {
        await using var fake = await FakeMisaServer.StartAsync();
        await using var factory = CreateFactory(fake.BaseUrl);
        using var client = factory.CreateClient();

        var invoice = SampleAmendmentDto.CreateInvoice("fresh-validation");
        // Empty ChangeReason triggers FR-060 pre-MISA validation rejection.
        var request = new ReplacementRequestDto(invoice, Ref, "   ", true);

        using var response = await client.PostAsJsonAsync("/api/invoices/replacement?withCode=true", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AmendmentResultDto>();
        Assert.NotNull(body);
        Assert.Equal("Validation", body!.ErrorCategory);
        Assert.NotNull(body.Failures);
        Assert.Contains(body.Failures!, f => f.RuleId == "FR-060");
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

using System.Net;
using System.Net.Http.Json;
using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Pdf;
using MisaConnect.EInvoice.Domain.Templates;
using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.Api;

public class InvoiceEndpointsDeleteTests
{
    [Fact]
    public async Task Deleted_returns_200_with_status_Deleted()
    {
        var stub = new StubClient(new DeleteResponse(true, null, null));
        await using var factory = new WebFactory(stub);
        using var client = factory.CreateClient();

        using var response = await client.DeleteAsync("/api/invoices/abc-123?withCode=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DeleteEnvelope>();
        Assert.NotNull(body);
        Assert.Equal("abc-123", body!.refId);
        Assert.Equal("Deleted", body.status);
    }

    [Fact]
    public async Task NotDeletable_returns_409_Conflict()
    {
        var stub = new StubClient(new DeleteResponse(false, "InvalidTransactionID", "Hóa đơn đã phát hành nên không thể xóa."));
        await using var factory = new WebFactory(stub);
        using var client = factory.CreateClient();

        using var response = await client.DeleteAsync("/api/invoices/abc-123?withCode=true");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DeleteEnvelope>();
        Assert.NotNull(body);
        Assert.Equal("NotDeletable", body!.status);
        Assert.Equal("InvalidTransactionID", body.errorCode);
        Assert.Null(body.message);
    }

    [Fact]
    public async Task NotFound_returns_404()
    {
        var stub = new StubClient(new DeleteResponse(false, "InvalidTransactionID", "RefID không tồn tại."));
        await using var factory = new WebFactory(stub);
        using var client = factory.CreateClient();

        using var response = await client.DeleteAsync("/api/invoices/abc-123?withCode=true");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DeleteEnvelope>();
        Assert.Equal("NotFound", body!.status);
        Assert.Null(body.message);
    }

    [Fact]
    public async Task NotFound_body_omits_message_by_default()
    {
        var stub = new StubClient(new DeleteResponse(false, "InvalidTransactionID", "RefID không tồn tại."));
        await using var factory = new WebFactory(stub);
        using var client = factory.CreateClient();

        using var response = await client.DeleteAsync("/api/invoices/abc-123?withCode=true");

        var body = await response.Content.ReadFromJsonAsync<DeleteEnvelope>();
        Assert.Null(body!.message);
    }

    [Fact]
    public async Task AuthFailed_returns_401()
    {
        var stub = new StubClient(new MeInvoiceException(MeInvoiceErrorCategory.Authentication, "UnAuthorize"));
        await using var factory = new WebFactory(stub);
        using var client = factory.CreateClient();

        using var response = await client.DeleteAsync("/api/invoices/abc-123?withCode=true");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Configuration_returns_400()
    {
        var stub = new StubClient(new DeleteResponse(false, "LicenseInfo_Expired", "License expired."));
        await using var factory = new WebFactory(stub);
        using var client = factory.CreateClient();

        using var response = await client.DeleteAsync("/api/invoices/abc-123?withCode=true");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MisaThrottled_returns_503_with_Retry_After()
    {
        var stub = new StubClient(new MeInvoiceException(MeInvoiceErrorCategory.MisaThrottled, "TooManyRequests"));
        await using var factory = new WebFactory(stub);
        using var client = factory.CreateClient();

        using var response = await client.DeleteAsync("/api/invoices/abc-123?withCode=true");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("2", response.Headers.GetValues("Retry-After").FirstOrDefault());
    }

    [Fact]
    public async Task MisaUnavailable_returns_502()
    {
        var stub = new StubClient(new MeInvoiceException(MeInvoiceErrorCategory.MisaUnavailable, "ServiceUnavailable"));
        await using var factory = new WebFactory(stub);
        using var client = factory.CreateClient();

        using var response = await client.DeleteAsync("/api/invoices/abc-123?withCode=true");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task TransportFailed_returns_502()
    {
        var stub = new StubClient(new MeInvoiceException(MeInvoiceErrorCategory.TransportFailed, "ConnectionFailed"));
        await using var factory = new WebFactory(stub);
        using var client = factory.CreateClient();

        using var response = await client.DeleteAsync("/api/invoices/abc-123?withCode=true");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task MisaUnknown_returns_502()
    {
        var stub = new StubClient(new DeleteResponse(false, "WildcardCode", "Weird."));
        await using var factory = new WebFactory(stub);
        using var client = factory.CreateClient();

        using var response = await client.DeleteAsync("/api/invoices/abc-123?withCode=true");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task Missing_withCode_returns_400()
    {
        var stub = new StubClient(new DeleteResponse(true, null, null));
        await using var factory = new WebFactory(stub);
        using var client = factory.CreateClient();

        using var response = await client.DeleteAsync("/api/invoices/abc-123");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record DeleteEnvelope(string? refId, string status, string? errorCode, string? field, string? message);

    private sealed class StubClient : IMeInvoiceClient
    {
        private readonly DeleteResponse? _response;
        private readonly MeInvoiceException? _exception;
        public StubClient(DeleteResponse r) => _response = r;
        public StubClient(MeInvoiceException ex) => _exception = ex;

        public Task<AccessToken> AcquireTokenAsync(CancellationToken ct) => Task.FromResult(new AccessToken("t", DateTimeOffset.UtcNow.AddDays(1)));
        public Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct)
        {
            if (_exception is not null) throw _exception;
            return Task.FromResult(_response!);
        }
    }
}

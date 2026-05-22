using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Application.Results;
using MisaConnect.EInvoice.Client;
using MisaConnect.EInvoice.Client.DependencyInjection;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Pdf;
using MisaConnect.EInvoice.Domain.Templates;
using MisaConnect.EInvoice.Infrastructure.Configuration;
using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.Client;

public class EInvoiceClientDeleteTests
{
    [Fact]
    public async Task Delegates_to_use_case_with_parsed_RefId()
    {
        var stub = new RecordingClient(new DeleteResponse(true, null, null));
        var sp = BuildContainer(stub);
        var client = sp.GetRequiredService<IMisaEInvoiceClient>();

        var outcome = await client.DeleteDraftAsync("abc-123", invoiceWithCode: true);

        Assert.Equal(DeleteDraftStatus.Deleted, outcome.Status);
        Assert.Equal("abc-123", stub.LastRefId?.Value);
        Assert.True(stub.LastInvoiceWithCode);
    }

    [Fact]
    public async Task Empty_refId_throws_MeInvoiceException_Validation()
    {
        var stub = new RecordingClient(new DeleteResponse(true, null, null));
        var sp = BuildContainer(stub);
        var client = sp.GetRequiredService<IMisaEInvoiceClient>();

        var ex = await Assert.ThrowsAsync<MeInvoiceException>(() => client.DeleteDraftAsync("", invoiceWithCode: true));
        Assert.Equal(MeInvoiceErrorCategory.Validation, ex.Category);
    }

    private static ServiceProvider BuildContainer(IMeInvoiceClient stub)
    {
        var services = new ServiceCollection();
        services.AddEInvoiceClient(opts =>
        {
            opts.Environment = MeInvoiceEnvironment.Sandbox;
            opts.BaseUrl = "https://testapi.meinvoice.vn/api/integration";
            opts.TaxCode = "0000000000";
            opts.AppId = "267";
            opts.UserName = "u";
            opts.Password = "p";
        });
        // Replace the decorator-wrapped IMeInvoiceClient with our stub.
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(IMeInvoiceClient))
            {
                services.RemoveAt(i);
            }
        }
        services.AddScoped(_ => stub);
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        return services.BuildServiceProvider();
    }

    private sealed class RecordingClient : IMeInvoiceClient
    {
        private readonly DeleteResponse _response;
        public RefId? LastRefId { get; private set; }
        public bool LastInvoiceWithCode { get; private set; }

        public RecordingClient(DeleteResponse r) => _response = r;

        public Task<AccessToken> AcquireTokenAsync(CancellationToken ct) => Task.FromResult(new AccessToken("t", DateTimeOffset.UtcNow.AddDays(1)));
        public Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct)
        {
            LastRefId = refId;
            LastInvoiceWithCode = invoiceWithCode;
            return Task.FromResult(_response);
        }
    }
}

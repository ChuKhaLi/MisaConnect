using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Client;
using MisaConnect.EInvoice.Client.DependencyInjection;
using MisaConnect.EInvoice.Client.Dtos;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Pdf;
using MisaConnect.EInvoice.Domain.Templates;
using MisaConnect.EInvoice.Infrastructure.Configuration;
using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.Client;

public class EInvoiceClientTicketTemplatesTests
{
    [Fact]
    public async Task Maps_request_and_rows_through_the_facade()
    {
        var stub = new RecordingClient([new TicketTemplate("tt-1", "Vé xe", "5C26GAA", false, 2, 5, null, true, true)]);
        using var sp = BuildContainer(stub);
        var client = sp.GetRequiredService<IMisaEInvoiceClient>();

        var rows = await client.ListTicketTemplatesAsync(
            new TicketTemplateListRequestDto(WithCode: false, Start: 20, Length: 50, FromCashRegister: true),
            TestContext.Current.CancellationToken);

        Assert.Equal(new TicketTemplateListRequest(false, 20, 50, true), stub.LastRequest);
        var row = Assert.Single(rows);
        Assert.Equal(new TicketTemplateDto("tt-1", "Vé xe", "5C26GAA", false, 2, 5, null, true, true), row);
    }

    [Fact]
    public async Task Null_request_uses_documented_defaults()
    {
        var stub = new RecordingClient([]);
        using var sp = BuildContainer(stub);
        var client = sp.GetRequiredService<IMisaEInvoiceClient>();

        await client.ListTicketTemplatesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(new TicketTemplateListRequest(true, 0, 20, false), stub.LastRequest);
    }

    [Fact]
    public async Task Invalid_request_throws_Validation_without_calling_MISA()
    {
        var stub = new RecordingClient([]);
        using var sp = BuildContainer(stub);
        var client = sp.GetRequiredService<IMisaEInvoiceClient>();

        var ex = await Assert.ThrowsAsync<MeInvoiceException>(() =>
            client.ListTicketTemplatesAsync(new TicketTemplateListRequestDto(Start: -1), TestContext.Current.CancellationToken));

        Assert.Equal(MeInvoiceErrorCategory.Validation, ex.Category);
        Assert.Null(stub.LastRequest);
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

    private sealed class RecordingClient(IReadOnlyList<TicketTemplate> rows) : IMeInvoiceClient
    {
        public TicketTemplateListRequest? LastRequest { get; private set; }

        public Task<IReadOnlyList<TicketTemplate>> ListTicketTemplatesAsync(TicketTemplateListRequest request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(rows);
        }

        public Task<AccessToken> AcquireTokenAsync(CancellationToken ct) => Task.FromResult(new AccessToken("t", DateTimeOffset.UtcNow.AddDays(1)));
        public Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
    }
}

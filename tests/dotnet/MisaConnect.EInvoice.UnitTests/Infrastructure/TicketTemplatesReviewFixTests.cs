using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Infrastructure.Configuration;
using MisaConnect.EInvoice.Infrastructure.MeInvoice;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Infrastructure;

/// <summary>
/// 2.10.0 final-review fixes for <c>/webapp/templates/ticket</c>: an unexpected wire shape surfaces as
/// <see cref="MeInvoiceException"/> (never a raw JsonException), numbers sent as strings are read, and
/// rows without a TicketTemplateID are skipped (as the TypeScript SDK does).
/// </summary>
public class TicketTemplatesReviewFixTests
{
    [Theory]
    [InlineData("""{"success":true,"data":"not json","errorCode":null}""")]
    [InlineData("""{"success":true,"data":"{\"TicketTemplateID\":\"tt-1\"}","errorCode":null}""")]
    [InlineData("""{"success":true,"data":"[{\"TicketTemplateID\":\"tt-1\",\"Inactive\":\"no\"}]","errorCode":null}""")]
    public async Task Unexpected_wire_shape_throws_MeInvoiceException_MisaUnknown(string envelope)
    {
        var client = MakeClient(envelope);

        var ex = await Assert.ThrowsAsync<MeInvoiceException>(() =>
            client.ListTicketTemplatesAsync(new TicketTemplateListRequest(), default));

        Assert.Equal(MeInvoiceErrorCategory.MisaUnknown, ex.Category);
        Assert.Equal("InvalidResponse", ex.RawErrorCode);
    }

    [Fact]
    public async Task Numbers_sent_as_strings_are_read()
    {
        var client = MakeClient("""{"success":true,"data":"[{\"TicketTemplateID\":\"tt-1\",\"TicketType\":\"3\",\"TemplateType\":\"5\"}]","errorCode":null}""");

        var row = Assert.Single(await client.ListTicketTemplatesAsync(new TicketTemplateListRequest(), default));

        Assert.Equal(3, row.TicketType);
        Assert.Equal(5, row.TemplateType);
    }

    [Fact]
    public async Task Rows_without_TicketTemplateID_are_skipped()
    {
        var client = MakeClient("""{"success":true,"data":"[{\"TicketTemplateID\":\"tt-1\"},{\"TemplateName\":\"no id\"},{\"TicketTemplateID\":null},{\"TicketTemplateID\":\"\"},null]","errorCode":null}""");

        var rows = await client.ListTicketTemplatesAsync(new TicketTemplateListRequest(), default);

        Assert.Equal(["tt-1"], rows.Select(r => r.TicketTemplateID));
    }

    private static MeInvoiceClient MakeClient(string envelope)
    {
        var http = new HttpClient(new StaticHandler(envelope))
        {
            BaseAddress = new Uri("https://testapi.meinvoice.vn/api/integration/"),
        };
        return new MeInvoiceClient(http, Options.Create(new MisaEInvoiceOptions
        {
            Environment = MeInvoiceEnvironment.Sandbox,
            BaseUrl = "https://testapi.meinvoice.vn/api/integration",
            TaxCode = "0000000000",
            AppId = "267",
            UserName = "u",
            Password = "p",
        }));
    }

    private sealed class StaticHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }
}

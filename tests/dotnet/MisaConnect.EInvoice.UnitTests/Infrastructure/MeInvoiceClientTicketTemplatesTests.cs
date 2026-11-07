using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Infrastructure.Configuration;
using MisaConnect.EInvoice.Infrastructure.MeInvoice;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Infrastructure;

/// <summary>
/// Wire shape of <c>POST /webapp/templates/ticket</c> (MISA API document §3.2 / §8.2): route,
/// all four documented query parameters, the credential body shared with <c>/webapp/templates</c>,
/// and mapping of the Ticket rows.
/// </summary>
public class MeInvoiceClientTicketTemplatesTests
{
    private const string TwoRows =
        """{"success":true,"data":"[{\"TicketTemplateID\":\"tt-1\",\"TemplateName\":\"Vé xe\",\"InvSeries\":\"5C26GAA\",\"Inactive\":false,\"TicketType\":2,\"TemplateType\":5,\"ServiceName\":\"Vận tải\",\"IsInheritFromOldTemplate\":true,\"IsTicketCode\":true},{\"TicketTemplateID\":\"tt-2\",\"TemplateName\":\"Vé cũ\",\"InvSeries\":\"5C25GAB\",\"Inactive\":true,\"TemplateType\":5}]","errorCode":null}""";

    [Fact]
    public async Task Posts_to_ticket_route_with_all_four_query_params()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var client = MakeClient(r =>
        {
            captured = r;
            body = r.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(TwoRows);
        });

        await client.ListTicketTemplatesAsync(new TicketTemplateListRequest(false, 40, 100, true), default);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Contains(
            "webapp/templates/ticket?invoiceWithCode=false&start=40&length=100&invoiceCalcu=true",
            captured.RequestUri!.ToString());
        using var doc = JsonDocument.Parse(body!);
        var props = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name.ToLowerInvariant(), p => p.Value.GetString());
        Assert.Equal("0000000000-267", props["taxcode"]);
        Assert.Equal("u", props["username"]);
    }

    [Fact]
    public async Task Defaults_send_documented_defaults()
    {
        HttpRequestMessage? captured = null;
        var client = MakeClient(r => { captured = r; return Json(TwoRows); });

        await client.ListTicketTemplatesAsync(new TicketTemplateListRequest(), default);

        Assert.Contains(
            "webapp/templates/ticket?invoiceWithCode=true&start=0&length=20&invoiceCalcu=false",
            captured!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Maps_wire_rows()
    {
        var client = MakeClient(_ => Json(TwoRows));

        var rows = await client.ListTicketTemplatesAsync(new TicketTemplateListRequest(), default);

        Assert.Equal(2, rows.Count);
        var full = rows[0];
        Assert.Equal("tt-1", full.TicketTemplateID);
        Assert.Equal("Vé xe", full.TemplateName);
        Assert.Equal("5C26GAA", full.InvSeries);
        Assert.True(full.IsActive);
        Assert.Equal(2, full.TicketType);
        Assert.Equal(5, full.TemplateType);
        Assert.Equal("Vận tải", full.ServiceName);
        Assert.True(full.IsInheritFromOldTemplate);
        Assert.True(full.IsTicketCode);

        var sparse = rows[1];
        Assert.False(sparse.IsActive);
        Assert.Equal(0, sparse.TicketType);
        Assert.Null(sparse.ServiceName);
        Assert.False(sparse.IsInheritFromOldTemplate);
        Assert.False(sparse.IsTicketCode);
    }

    [Theory]
    [InlineData("""{"success":true,"data":null,"errorCode":null}""")]
    [InlineData("""{"success":true,"data":"","errorCode":null}""")]
    public async Task Null_or_empty_data_returns_empty_list(string envelope)
    {
        var client = MakeClient(_ => Json(envelope));

        var rows = await client.ListTicketTemplatesAsync(new TicketTemplateListRequest(), default);

        Assert.Empty(rows);
    }

    [Theory]
    [InlineData("""{"success":false,"errorCode":"InvalidTokenCode","error":"InvalidTokenCode"}""")]
    [InlineData("""{"success":false,"errorCode":"UnknownCode","error":"UnknownCode"}""")]
    public async Task Failure_envelope_maps_like_templates(string envelope)
    {
        var client = MakeClient(_ => Json(envelope));

        var templates = await Assert.ThrowsAsync<MeInvoiceException>(() => client.ListTemplatesAsync(true, default));
        var tickets = await Assert.ThrowsAsync<MeInvoiceException>(() =>
            client.ListTicketTemplatesAsync(new TicketTemplateListRequest(), default));

        Assert.Equal(templates.Category, tickets.Category);
        Assert.Equal(templates.RawErrorCode, tickets.RawErrorCode);
    }

    private static MeInvoiceClient MakeClient(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var http = new HttpClient(new CapturingHandler(respond))
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

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}

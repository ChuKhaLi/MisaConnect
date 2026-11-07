using System.Net;
using System.Text.Json;
using MisaConnect.EInvoice.IntegrationTests.Api;
using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.MisaFake;

/// <summary>
/// <c>GET /api/templates/ticket</c> on the sample API through the real
/// <c>MeInvoiceClient</c> + handler chain against the in-process fake MISA server.
/// </summary>
public class TicketTemplatesFakeTests
{
    [Fact]
    public async Task Lists_ticket_templates_through_the_real_adapter()
    {
        await using var fake = await FakeMisaServer.StartAsync();
        await using var factory = CreateFactory(fake.BaseUrl);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/api/templates/ticket?withCode=false&start=5&length=10&fromCashRegister=true",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var row = Assert.Single(doc.RootElement.GetProperty("ticketTemplates").EnumerateArray());
        Assert.Equal("tt-fake", row.GetProperty("ticketTemplateId").GetString());
        Assert.True(row.GetProperty("isActive").GetBoolean());
        Assert.Equal("Fake service", row.GetProperty("serviceName").GetString());

        var call = Assert.Single(fake.CapturedRequests, r => r.Path.EndsWith("/webapp/templates/ticket", StringComparison.Ordinal));
        Assert.Equal("POST", call.Method);
        Assert.Equal("?invoiceWithCode=false&start=5&length=10&invoiceCalcu=true", call.QueryString);
    }

    [Fact]
    public async Task Invalid_length_is_rejected_before_MISA_is_called()
    {
        await using var fake = await FakeMisaServer.StartAsync();
        await using var factory = CreateFactory(fake.BaseUrl);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/templates/ticket?length=101", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(fake.CapturedRequests, r => r.Path.EndsWith("/webapp/templates/ticket", StringComparison.Ordinal));
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
        }).WithBaseAddressOverride(fakeBaseUrl);
}
